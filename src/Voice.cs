using System;
using System.Collections.Generic;

namespace Sightline;

// ═══════════════════════════════════════════════════════════════════════════════════════════
//  VOICE — PROGRAM RESONANCE, WAVE C1.  The game's words.
//
//  SIGHTLINE shipped a great deal of attachment machinery — callsigns, ranks, earned traits and
//  nicknames, scars, bonds, vendettas, a bleed-out window, a memorial, a veteran reserve — and
//  then never said a single sentence about any of it. Measured over 16 campaigns: 146 soldiers
//  went down, 74 bled out, 22 were finished while down, ONE was revived. The only telling was a
//  floating damage number and a name on a list.
//
//  This file is the LIGHT FRAME that finally speaks: mission briefings, faction dossiers, region
//  names, in-fight soldier barks, and a run epilogue. It is deliberately a frame, not a story —
//  see docs/DESIGN.md §1 (the recorded amendment) for what that means and where the limits are.
//
//  ── THE HARD CONSTRAINT (read before adding a single word) ────────────────────────────────────
//  Every word generated here must take ZERO draws from the shared Util.Rng stream. The project's
//  entire balance methodology rests on common-random-number pairing: two runs on the same slot
//  seed must be byte-identical, and SIGHTLINE_PAIRTEST / a paired SIGHTLINE_BALANCE batch are the
//  gates. An earlier wave shipped audio synthesis that drew from Util.Rng and silently perturbed
//  gameplay; that must never happen again. Therefore:
//    * Everything that must REPLAY identically (region names, briefings, the epilogue) is derived
//      by AVALANCHE HASH from MapSeed via Util.Hash3 — a pure function, zero draws, by construction.
//    * The only live stream here is `_rng`, a DEDICATED Random re-seeded per mission from that same
//      hash. It feeds bark variant choice ONLY, and nothing it produces is ever read by combat, AI,
//      mission generation, or persistence — it is text that lands in Stats.CombatLog.
//    * SIGHTLINE_VOICETEST asserts the separation directly: it snapshots the shared stream, runs
//      every generator in this file, and requires the shared stream's next draws to be unchanged.
//  Never reach for Util.Rng / Util.Choice / Util.RandInt in this file. Use Pick(...) or Hash3.
//
//  WRITING BAR: terse, military, unsentimental. No exclamation marks, no purple, no grimdark
//  boilerplate. A bark is a person on a radio under fire, not a narrator. Fewer, better lines.
//  Every template slot must be un-fillable with nonsense: a soldier with no bond can never draw
//  a bond line (the CALLER supplies the partner; a null partner means the beat never fires).
// ═══════════════════════════════════════════════════════════════════════════════════════════
public static class Voice
{
    // ─────────────────────────────────────────────────────────────────────────────────────
    //  DEDICATED RNG (see the header). Bark variety only.
    // ─────────────────────────────────────────────────────────────────────────────────────
    static Random _rng = new Random(unchecked((int)0x56_4F_49_43));   // "VOIC"; replaced per mission

    /// Deterministic index into `n` from the dedicated stream. The ONLY randomness in this file.
    static int Roll(int n) => n <= 1 ? 0 : _rng.Next(n);

    /// Deterministic index into `n` from a seed triple — ZERO draws from any stream.
    static int Pick(int n, int a, int b, int c) => n <= 1 ? 0 : (int)(Util.Hash3(a, b, c) % (uint)n);

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  REGIONS — the campaign map stops being a debug graph.
    //  Eight curated names per BIOME (never a two-pool mad-lib: a generated "EMBER GLACIER" is
    //  exactly the nonsense the writing bar forbids). A run's six missions always land on six
    //  DISTINCT biomes (Biome.IndexFor is (seed + m - 1) % 8 over an 8-entry table), so a run can
    //  never show the same region name twice. Pure function of (mission, MapSeed): zero draws, and
    //  it round-trips on load exactly like the map itself.
    // ─────────────────────────────────────────────────────────────────────────────────────
    static readonly string[][] Regions =
    {
        // STEEL — cold industrial.
        new[] { "COLD HARBOR", "DRYDOCK NINE", "IRON REACH", "GREY YARDS",
                "NORTH SPAN", "HULL SEVEN", "RIVET GATE", "BREAKWATER" },
        // ARID — sun-baked.
        new[] { "SALT FLATS", "BONE WASH", "LOW MESA", "DUST REACH",
                "SHALE CUT", "ANVIL RIDGE", "DRY BASIN", "REDLINE PASS" },
        // TUNDRA — frozen.
        new[] { "WHITE FORD", "HOAR RIDGE", "GLASS LAKE", "NORTHWIND",
                "FROSTGATE", "PALE CROSSING", "ICEFALL", "SILENT SHELF" },
        // VERDANT — overgrown.
        new[] { "GREEN HOLLOW", "BRIAR BASIN", "THORNGATE", "OVERGROWTH",
                "WILD ACRE", "MOSSWALL", "FERN CUT", "DEEPGROVE" },
        // ASH — burnt out.
        new[] { "CINDER ROW", "GREY MILE", "BURNT LADDER", "SOOTFALL",
                "REMNANT FIELD", "ASHGATE", "KILN DISTRICT", "SCORCH LINE" },
        // VOID — lightless.
        new[] { "NULL SHELF", "LONG DARK", "BLACKVAULT", "QUIET DEEP",
                "UMBRA HALL", "STARLESS REACH", "HOLLOW SPINE", "NIGHTWELL" },
        // NEON — arcology.
        new[] { "STACK NINE", "WIRE DISTRICT", "COLDLIGHT", "SPLICE ROW",
                "TERMINAL SIX", "GLASSWORKS", "BLUE MILE", "LATTICE WARD" },
        // MAGMA — foundry.
        new[] { "SLAG THROAT", "EMBER CUT", "FOUNDRY FLOOR", "CALDERA",
                "BASALT STEP", "FIRE ROW", "BLASTGATE", "CINDERCONE" },
    };

    /// The named region mission `n` of this run is fought over. Deterministic from MapSeed;
    /// biome-true (a TUNDRA room never reads as EMBER CUT).
    public static string RegionName(int mission, int mapSeed)
    {
        int bi = Biome.IndexFor(Math.Max(1, mission), mapSeed);
        bi = ((bi % Regions.Length) + Regions.Length) % Regions.Length;
        var pool = Regions[bi];
        return pool[Pick(pool.Length, mapSeed, mission, 0x5247)];   // "RG"
    }

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  ARENAS — the authored layouts finally get spoken names.
    //  Index-aligned with Maps.Layouts; the names are the ones already written in that file's
    //  per-layout comments, and each clause is a terse read of what the ground actually does.
    //  VOICETEST asserts Arenas.Length == Maps.Layouts.Length, so adding an arena without naming
    //  it fails loudly instead of silently briefing the player about the wrong terrain.
    // ─────────────────────────────────────────────────────────────────────────────────────
    static readonly (string Name, string Clause)[] Arenas =
    {
        ("PLAZA",      "A raised platform ringed with cover"),
        ("GAUNTLET",   "Staggered lanes flanking a central spine"),
        ("PILLARS",    "A column field with open aisles and live barrels"),
        ("CHEVRON",    "A diagonal wall and a raised redoubt"),
        ("CITADEL",    "A fortified bunker with one doorway"),
        ("ZIGGURAT",   "A stepped mound; the summit commands it"),
        ("CROSSROADS", "Pillar rows channel every sightline"),
        ("FOXHOLES",   "A close-quarters warren with two strongpoints"),
        ("RIDGE",      "A diagonal ridge under a commanding peak"),
        ("RUINS",      "Open ground inside shattered walls"),
        ("THICKET",    "Organic cover clumps and winding lanes"),
        ("BASTION",    "A walled keep holds the centre"),
        ("CHASM",      "A wall of cover splits the field north to south"),
        ("SPUR",       "A high spine runs corner to corner"),
        ("HOOK",       "One fortified strongpoint, one open flank"),
        ("GRID",       "A lattice of racks; every lane is short"),
        ("FORGE",      "A casting platform stands over the floor"),
        ("CONDUIT",    "Bunkers bank a horizontally split complex"),
        ("PALISADE",   "A mid-field screen breaks the long shots"),
        ("TERRACE",    "A firing terrace over a split level"),
        ("WISHBONE",   "Two walls fan out from a central spine"),
        ("HIGHLAND",   "An off-centre vantage owns the field"),
        ("APPROACH",   "Dense cover north, open ground south"),
        ("KILLBOX",    "An open plaza inside a broken ring"),
        ("TRENCHES",   "Parallel lines of low cover, offset"),
        ("GARRISON",   "One wall across the mid-field, pierced"),
        ("PINNACLE",   "Two peaks; the height decides it"),
        ("REFINERY",   "Barrels clustered through the tank farm"),
        ("CRUCIBLE",   "A rigged chokepoint between two bastions"),
        ("STEPWELL",   "A stepped pyramid with a single summit"),
        ("COLONNADE",  "A long gallery of paired pillars"),
        ("ENTRENCHED", "A zig-zag warren facing open ground"),
        ("CAUSEWAY",   "A raised bridge is the only good crossing"),
        ("REDANS",     "A sawtooth of angular walls"),
        ("DONJON",     "A keep with a single way in"),
    };

    /// Spoken name of the authored arena in play (`Mission.AppliedLayout`); -1 = the procedural
    /// fallback, which is honestly named rather than dressed up.
    public static string ArenaName(int layout)
        => layout >= 0 && layout < Arenas.Length ? Arenas[layout].Name : "OPEN GROUND";

    /// One clause describing what the ground does. Always a complete, capitalised fragment.
    public static string ArenaClause(int layout)
        => layout >= 0 && layout < Arenas.Length ? Arenas[layout].Clause : "Open ground with scattered cover";

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  FACTIONS — three opponents that finally have a face.
    //  Each dossier's MECHANICAL sentence is anchored on the real constant the combat code
    //  applies (Combat.LegionCloseAim / LegionCloseCrit / WardenLongAim, the Syndicate see-over-low
    //  rule, and the counter-prep that cancels each), so the fiction can never drift from the rules.
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// A two-or-three word epithet — the map hint, the briefing and the codex all read this.
    public static string FactionEpithet(Faction f) => f switch
    {
        Faction.Syndicate => "THE SYNDICATE",
        Faction.Legion    => "THE LEGION",
        Faction.Wardens   => "THE WARDENS",
        _                 => "LOCAL FORCES",
    };

    /// The defence low cover WOULD be worth, read straight off the resolver's own CoverInfo rather
    /// than retyped here. The Syndicate line quotes this number to say what their optics take away,
    /// so a tuning change to cover can never leave the briefing quietly lying about it.
    static int LowCoverDefense => new Grid.CoverInfo { Level = 1 }.Defense;

    /// The briefing's opposition line: who holds the ground and the one thing they do to you.
    ///
    /// A3 REWRITE. C1 shipped these as a template and said so: three of the four read
    /// "<Faction> ground: a, b, c. <rule>." — which is fine once and obvious by run four, because
    /// the briefing card is the same three-line shape every mission and the middle line was the
    /// only slot with any room to vary. Each line now has its OWN sentence shape (a prohibition,
    /// a thesis, an observation, a shrug) so four runs read as four briefings.
    ///
    /// What did NOT change is the load-bearing half: every number below is interpolated from the
    /// constant the resolver actually applies (Combat.LegionCloseAim / LegionCloseCrit /
    /// WardenLongAim, Unit.CloseRange / LongRange, and cover's own Defense). That is what makes
    /// this line worth reading instead of decoration, and VOICETEST asserts each value is present.
    public static string FactionBriefLine(Faction f) => f switch
    {
        Faction.Syndicate =>
            $"Do not trust low cover here. Syndicate optics shoot over it, and the {LowCoverDefense} it should cost them is worth nothing. High walls, or height.",
        Faction.Legion =>
            $"Range is the whole fight. Let the Legion inside {Unit.CloseRange} tiles and it picks up +{Combat.LegionCloseAim} aim and +{Combat.LegionCloseCrit} crit. Hold them off, or brace them.",
        Faction.Wardens =>
            $"The Wardens will not come to you. Past {Unit.LongRange} tiles their rifles gain +{Combat.WardenLongAim} aim, so every turn in the open is one they are paid for.",
        _ =>
            "No colours flying. Deserters and contractors holding what they stand on. No faction rule applies — fight the pods you can see.",
    };

    /// The FIELD MANUAL dossier. Three short paragraphs: who they are, how they fight, how you
    /// beat them. Each fighting line names the real rule; each counter names the real item.
    public static string FactionDossier(Faction f) => f switch
    {
        Faction.Syndicate =>
            "A corporate security arm that buys what it cannot train: optics, drones, barrier plate. It fights " +
            "like an installation defending itself — screens forward, shields anchoring, drones over the top.\n" +
            "FIELD RULE: Syndicate shooters see OVER low cover. A block that would blunt anyone else's aim by 20 " +
            "does nothing against them.\n" +
            $"COUNTER: high cover, height, or HARDENED OPTICS bought as counter-prep. Kill the AEGIS and the wall " +
            "stops walking. Their capstone is the SPYMASTER: a shield arc behind a screen cell — flank it or take height.",
        Faction.Legion =>
            "A conscript war-host that solves every problem by arriving. It does not trade fire at distance; it " +
            "spends bodies to close, then breaks you at arm's length.\n" +
            $"FIELD RULE: inside {Unit.CloseRange} tiles a Legion attacker gains +{Combat.LegionCloseAim} aim and " +
            $"+{Combat.LegionCloseCrit} crit. The danger is entirely a function of range.\n" +
            "COUNTER: overwatch the approach, BRACE the runner, and keep the distance you were given. REACTIVE " +
            "PLATING blunts the alpha when you know they are coming. Their capstone is the SIEGELORD: telegraphed " +
            "strikes force relocation — keep moving and kill it fast.",
        Faction.Wardens =>
            "A garrison order that holds ground for a living. Disciplined, patient, dug in — it would rather shoot " +
            "you from a hill for six turns than take one.\n" +
            $"FIELD RULE: at long range a Warden attacker gains +{Combat.WardenLongAim} aim. Their bombards and " +
            "spotters exist to make you stand still in the open.\n" +
            "COUNTER: FIELD SMOKE, broken lanes, and forward pressure — every tile you close is aim off their rifles. " +
            "Their capstone is the WARLORD: anchored by banner and medic — break the retinue, then burst the brick.",
        _ =>
            "No standing colours. Deserters, contractors and whatever the last three wars left behind, fighting for " +
            "the ground they are standing on.\n" +
            "FIELD RULE: none. A mixed force carries no faction warp — what you see on the board is what it does.\n" +
            "COUNTER: read the pods and fight the units, not the banner.",
    };

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  BRIEFINGS — three lines per campaign node, composed from data the game already had.
    //  place × opposition × task. Deterministic (no draws), so a reloaded save re-briefs the
    //  same way. Rendered by Hud.DrawBriefCard; auto-dismisses, dismissible, never hit-tested.
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// The task line: the objective, said the way a commander would say it.
    public static string ObjectiveLine(Objective o) => o switch
    {
        Objective.Eliminate  => "Clear the field. Nothing hostile walks off it.",
        Objective.Evac       => "Fight to the extraction zone and get every soldier inside it.",
        Objective.Hack       => "Take the terminal and hold it. The moment you touch it, they know.",
        Objective.Escort     => "Walk the asset out alive. It cannot take a hit, and it will not wait.",
        Objective.Sabotage   => "Three charges, all three planted. It goes loud on the first one.",
        Objective.Rescue     => "Cut the captive loose, then walk them to extraction.",
        Objective.Defend     => "Hold this ground. Waves are already inbound — the clock is the win.",
        Objective.Decapitate => "Kill the marked target. Peel the bodyguards first.",
        _                    => "Take the ground and hold it.",
    };

    /// Compose the three briefing lines for a campaign node.
    ///   mission   1-based mission number       mapSeed  Run.MapSeed (region derivation)
    ///   layout    Mission.AppliedLayout (-1 procedural)
    ///   finale    true only on the capstone hunt; bossName/bossClause then lead the opposition line
    public static string[] Brief(int mission, int mapSeed, int layout, Objective obj, Faction fac,
                                 bool finale, string bossName, string bossClause)
    {
        string place = $"{RegionName(mission, mapSeed)}. {ArenaClause(layout)}.";
        string foes = finale && !string.IsNullOrEmpty(bossName)
            ? $"The {bossName} is here, and its retinue with it: {bossClause}."
            : FactionBriefLine(fac);
        return new[] { place, foes, ObjectiveLine(obj) };
    }

    /// Header for the briefing card — matches the FIELD TIP chrome's "HEAD - CODE" shape.
    public static string BriefHead(Objective obj) => "BRIEFING - " + Codex.ObjectiveName(obj);

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  BARKS — the squad speaks, rarely, at beats that actually matter.
    //
    //  RATE LIMIT IS THE FEATURE. The combat log is load-bearing for "why did that happen"; a
    //  chatty squad would push the mechanical lines off it. Four hard gates, all enforced here
    //  except the first, which the caller owns:
    //    1. (caller) never while a tutorial lesson or a field tip card is on screen — wave T1
    //       owns those and they win, always.
    //    2. at most ONE bark per game turn.
    //    3. never the same speaker twice in a row.
    //    4. each beat kind fires at most ONCE per mission.
    //  Ceiling: six lines in a whole mission, and a typical mission speaks two or three.
    // ─────────────────────────────────────────────────────────────────────────────────────
    public enum Beat { FirstBlood, BondDown, PodRout, Stabilize, Vendetta, LastStanding }

    /// Whether a beat needs a second name ({0} = speaker's partner). A beat that needs one and
    /// is handed none simply does not fire — the "soldier with no bond gets a bond line" trap.
    static bool NeedsOther(Beat b) => b == Beat.BondDown;

    // A3: THE POOLS ARE SIX DEEP, NOT THREE.
    // C1 shipped three variants a beat and named the weakness itself: "widen the pools before
    // widening the beat list." With a ceiling of six lines a MISSION and a beat that fires once
    // each, three variants meant a returning player heard the same sentence on the same trigger
    // roughly every other run. Doubling the pool is the cheapest possible fix and costs the log
    // nothing. Deliberately NO new beats: the rate limit is the feature (see the gates above),
    // and more triggers would spend the budget the mechanical lines need.
    //
    // The bar has not moved. Every line is somebody on a radio with a rifle in their hands:
    // present tense, one or two clauses, no exclamation marks, no narration, nothing that
    // comments on the game. VOICETEST pins the pool depth, the duplicate check and the width.
    static readonly Dictionary<Beat, string[]> Lines = new()
    {
        [Beat.FirstBlood] = new[]
        {
            "First one's down.",
            "That's one. Keep moving.",
            "Contact confirmed. One down.",
            "Scratch one.",
            "One down. Watch your angles.",
            "Down. Next.",
        },
        [Beat.BondDown] = new[]
        {
            "{0} is down. Cover me.",
            "Get to {0}. Now.",
            "{0}, stay with me.",
            "{0} is hit. I'm going.",
            "That's {0} hit. Cover.",
            "{0}. Talk to me.",
        },
        [Beat.PodRout] = new[]
        {
            "They're breaking. Push.",
            "That's it, they're running.",
            "Line's cracked. Press it.",
            "They've had enough. Move up.",
            "Their nerve's gone. Go.",
            "Broken. Keep them broken.",
        },
        [Beat.Stabilize] = new[]
        {
            "Pressure on. Stay awake.",
            "You're not dying here.",
            "I've got you. Hold on.",
            "Bleeding's stopped.",
            "Still with us. Barely.",
            "Wound's packed. Lie still.",
        },
        [Beat.Vendetta] = new[]
        {
            "That's for last time.",
            "I remember this outfit.",
            "Been waiting for you lot.",
            "Same colours. Good.",
            "I owe this lot a bad day.",
            "We've met. It went badly.",
        },
        [Beat.LastStanding] = new[]
        {
            "I'm the last one up.",
            "Just me. Still shooting.",
            "Squad's down. Still here.",
            "Everyone's down but me.",
            "On my own out here.",
            "Down to me. Still working.",
        },
    };

    // per-mission bark state (reset by BeginMission)
    static readonly HashSet<Beat> _fired = new();
    static string _lastSpeaker;
    static int _lastBarkTurn = int.MinValue;

    /// Re-seed the dedicated stream and clear the per-mission bark budget. Called from
    /// Game.SetupMission. Seeded from (mapSeed, mission) so a replayed mission speaks the same
    /// lines — the paired-leg logs stay comparable — while different runs vary.
    public static void BeginMission(int mapSeed, int mission)
    {
        _rng = new Random(unchecked((int)Util.Hash3(mapSeed, mission, 0x564F)));   // "VO"
        _fired.Clear();
        _lastSpeaker = null;
        _lastBarkTurn = int.MinValue;
    }

    /// Try to speak. Returns the composed log line, or null when a gate refuses.
    /// `speaker` is the soldier's short callsign (never the nickname — the log is 12px and narrow).
    /// `other` is the second name a beat needs; null for beats that need none.
    public static string TryBark(Beat beat, string speaker, string other, int turn)
    {
        if (string.IsNullOrEmpty(speaker)) return null;
        if (NeedsOther(beat) && string.IsNullOrEmpty(other)) return null;   // never a bond line without a bond
        if (_fired.Contains(beat)) return null;                             // gate 4: once per mission
        if (turn == _lastBarkTurn) return null;                             // gate 2: one per turn
        if (speaker == _lastSpeaker) return null;                           // gate 3: no repeat speaker
        if (!Lines.TryGetValue(beat, out var pool) || pool.Length == 0) return null;

        string body = pool[Roll(pool.Length)];
        if (NeedsOther(beat)) body = body.Replace("{0}", other);
        _fired.Add(beat);
        _lastSpeaker = speaker;
        _lastBarkTurn = turn;
        return Compose(speaker, body);
    }

    /// The combat-log form of a bark. One place, so the self-test's width check measures exactly
    /// what the Hud draws.
    public static string Compose(string speaker, string body) => $"{speaker}: \"{body}\"";

    /// The Stats.LogEntry outcome tag barks carry. The Hud tints on it, and it can never be
    /// mistaken for a mechanical result (HIT/MISS/CRIT/GRAZE/KILL/DOWN).
    public const string LogTag = "VOICE";

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  EPILOGUE — five lines at the end card, generated from telemetry the card already has.
    //  This is where the unremarked dead finally get told: who fell, where, and what it cost.
    //  Deterministic (hash-picked, zero draws) and memoised, because the end card redraws it
    //  every frame. EXACTLY five lines, every slot with a non-empty fallback, all asserted.
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// Everything the epilogue reads, gathered by the caller so this file never touches Game.
    public struct RunFacts
    {
        public bool Win;
        public int Missions;            // missions actually cleared
        public int MapSeed;
        public int Heat;
        public int Kills;               // confirmed kills, whole run
        public int Survivors;           // living, non-VIP soldiers
        public string MvpName;          // top-kill survivor, or null
        public int MvpKills;
        public string TopEnemyName;     // bestiary name of the archetype that killed the most, or null
        public string TopEnemyCls;
        public int TopEnemyKills;
        public string BossName;         // the run's finale boss, or null
        public List<FallenRec> Memorial; // may be null/empty
    }

    static string _epiKey;
    static List<string> _epiLines;

    /// Five lines. Memoised on a cheap signature so a 60fps end card composes them once.
    public static List<string> Epilogue(RunFacts f)
    {
        int dead = f.Memorial?.Count ?? 0;
        string key = string.Join("|", f.Win ? 1 : 0, f.Missions, f.MapSeed, f.Heat, f.Kills,
                                 f.Survivors, f.MvpName ?? "-", f.MvpKills, f.TopEnemyCls ?? "-",
                                 f.TopEnemyKills, f.BossName ?? "-", dead);
        if (key == _epiKey && _epiLines != null) return _epiLines;
        _epiKey = key;
        _epiLines = Compose(f, dead);
        return _epiLines;
    }

    static List<string> Compose(RunFacts f, int dead)
    {
        int seed = f.MapSeed;
        var lines = new List<string>(5);

        // ── 1. THE FILE — what this run was, in one sentence. On a WIN the card's own subtitle
        //      already says "all N missions cleared", so this line spends its words on WHERE it
        //      ended and WHAT fell there rather than repeating the count back at the player.
        if (f.Win)
            lines.Add(!string.IsNullOrEmpty(f.BossName)
                ? $"The file closes at {RegionName(Run.MaxMissions, seed)}. The {f.BossName} fell last."
                : $"The file closes at {RegionName(Run.MaxMissions, seed)}.");
        else if (f.Missions <= 0)
            lines.Add($"The run ended on the first operation, at {RegionName(1, seed)}.");
        else
            lines.Add($"{f.Missions} operation{(f.Missions == 1 ? "" : "s")} cleared. It ended at " +
                      $"{RegionName(f.Missions + 1, seed)}, on the {Ordinal(f.Missions + 1)}.");

        // ── 2. THE COST — the count, plainly. No adjectives; the number is the sentence.
        if (dead == 0)
            lines.Add("Nobody was left behind. The whole roster came home from every field.");
        else if (dead == 1)
            lines.Add("One soldier did not come home.");
        else
            lines.Add($"{dead} soldiers did not come home.");

        // ── 3. THE NAME — one death, told properly. The costliest loss (most confirmed kills,
        //      ties broken by the later mission) gets the line; the rest are counted, not padded.
        //      This is the sentence the whole wave exists for.
        if (dead > 0)
        {
            FallenRec worst = f.Memorial[0];
            foreach (var r in f.Memorial)
                if (r.Kills > worst.Kills || (r.Kills == worst.Kills && r.Mission > worst.Mission)) worst = r;
            string where = RegionName(Math.Max(1, worst.Mission), seed);
            string who = $"{worst.Rank} {worst.Name}".Trim();
            lines.Add(worst.Kills > 0
                ? $"{who} fell at {where} with {worst.Kills} confirmed to their name."
                : $"{who} fell at {where} before the record had anything to say about them.");
        }
        else
        {
            lines.Add(f.Kills > 0
                ? $"{f.Kills} confirmed kills, and not one name for the wall."
                : "Not one name for the wall.");
        }

        // ── 4. WHAT IT TURNED ON — the thing that actually did the damage, or the soldier who did.
        //      Every branch below is grammatical AND TRUE at every count: "did most of it" is only
        //      said when it is a majority, and "every one of them" only when it is all of them.
        if (!string.IsNullOrEmpty(f.TopEnemyName) && f.TopEnemyKills > 0 && dead > 0)
        {
            int k = Math.Min(f.TopEnemyKills, dead);
            lines.Add(k >= dead
                ? $"Every one of them fell to the {f.TopEnemyName}."
                : k * 2 > dead
                    ? $"Most of them fell to the {f.TopEnemyName} — {k} of the {dead}."
                    : $"{k} of the {dead} fell to the {f.TopEnemyName}.");
        }
        else if (!string.IsNullOrEmpty(f.MvpName) && f.MvpKills > 0)
            lines.Add($"{f.MvpName} carried the shooting, {f.MvpKills} confirmed.");
        else if (f.Kills > 0)
            lines.Add($"{f.Kills} confirmed kills across the run, spread thin across the roster.");
        else
            lines.Add("Nothing on this run was decided by the shooting.");

        // ── 5. THE CLOSER — where it leaves them. HEAT is deliberately NOT appended: the card
        //      already carries a HEAT slab, and a stat tacked onto the last sentence reads like
        //      a receipt line rather than an ending.
        if (f.Survivors <= 0)
            lines.Add(dead > 0 ? "Nobody walked off the last field. The reserve remembers them."
                               : "Nobody walked off the last field.");
        else if (f.Win)
            lines.Add(f.Survivors == 1
                ? (!string.IsNullOrEmpty(f.MvpName) ? $"{f.MvpName} walked off the last field alone."
                                                    : "One soldier walked off the last field alone.")
                : (!string.IsNullOrEmpty(f.MvpName) ? $"{f.MvpName} led the other {f.Survivors - 1} off the last field."
                                                    : $"{f.Survivors} walked off the last field."));
        else
            lines.Add(dead > 0
                ? $"{f.Survivors} still standing when the order came to stop. They carry the rest."
                : $"{f.Survivors} still standing when the order came to stop.");
        return lines;
    }

    static string Ordinal(int n) => n switch
    {
        1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth",
        6 => "sixth", 7 => "seventh", 8 => "eighth", _ => $"{n}th",
    };

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  VOICETEST — SIGHTLINE_VOICETEST=1. The content + separation contract.
    //  Needs a window (Cfg.Measure hits the font atlas) — Program.cs opens a tiny one.
    // ─────────────────────────────────────────────────────────────────────────────────────

    /// Pixel budget available to a combat-log line: the panel's text column, from Hud.
    public const float LogTextW = Hud.LogTextWidth;
    public const int LogFontSize = 12;

    public static string SelfTest()
    {
        var fails = new List<string>();
        void Chk(bool ok, string msg) { if (!ok) fails.Add(msg); }
        bool Filled(string s) => !string.IsNullOrWhiteSpace(s) && s.IndexOf("{0}", StringComparison.Ordinal) < 0
                                 && s.IndexOf("  ", StringComparison.Ordinal) < 0;

        // ── (1) RNG SEPARATION — the contract the whole balance methodology rests on.
        // Snapshot the shared stream by drawing K values from a known seed; reseed; run EVERY
        // generator in this file; then draw K again. Any draw Voice took from Util.Rng shifts the
        // second sequence. Vacuous-PASS guard: the probe itself is verified to be sensitive by
        // running it once WITH a deliberate shared-stream draw and requiring it to differ.
        const int K = 24;
        int[] Probe(Action body)
        {
            Util.Reseed(1234567);
            var pre = new int[K];
            for (int i = 0; i < K; i++) pre[i] = Util.Rng.Next(1 << 20);
            Util.Reseed(1234567);
            body();
            var post = new int[K];
            for (int i = 0; i < K; i++) post[i] = Util.Rng.Next(1 << 20);
            return SeqEq(pre, post) ? pre : null;   // null == the body consumed shared draws
        }
        var clean = Probe(ExerciseEverything);
        Chk(clean != null, "RNG SEPARATION: generating voice content consumed draws from Util.Rng");
        var dirty = Probe(() => { Util.Rng.Next(); ExerciseEverything(); });
        Chk(dirty == null, "RNG probe is INSENSITIVE (a deliberate shared draw went undetected)");
        Util.Reseed(0);   // leave the shared stream on a clock seed, as found

        // ── (2) REGIONS — every pool full, biome-aligned, and a run's six regions all distinct.
        Chk(Regions.Length == Biome.All.Length, $"Regions has {Regions.Length} biome pools, Biome.All has {Biome.All.Length}");
        for (int b = 0; b < Regions.Length; b++)
        {
            Chk(Regions[b].Length >= 4, $"REGION pool {b} has only {Regions[b].Length} names");
            foreach (var nm in Regions[b]) Chk(Filled(nm), $"REGION pool {b} has an empty name");
        }
        var allRegions = new HashSet<string>();
        foreach (var pool in Regions) foreach (var nm in pool)
            Chk(allRegions.Add(nm), $"REGION name '{nm}' is used by two biomes");
        for (int s = 1; s <= 400; s++)
        {
            var seen = new HashSet<string>();
            for (int m = 1; m <= Run.MaxMissions; m++)
            {
                string rn = RegionName(m, s * 7919);
                Chk(Filled(rn), $"REGION empty at seed {s} mission {m}");
                Chk(seen.Add(rn), $"REGION '{rn}' repeats inside one run (seed {s * 7919})");
            }
        }

        // ── (3) ARENAS — one name+clause per authored layout, no gaps, fallback honest.
        Chk(Arenas.Length == Maps.Layouts.Length,
            $"Arenas table has {Arenas.Length} rows, Maps.Layouts has {Maps.Layouts.Length} — name the new arena");
        for (int i = 0; i < Arenas.Length; i++)
        {
            Chk(Filled(Arenas[i].Name), $"ARENA {i} empty name");
            Chk(Filled(Arenas[i].Clause), $"ARENA {i} empty clause");
        }
        Chk(Filled(ArenaName(-1)) && Filled(ArenaClause(-1)), "ARENA procedural fallback is empty");
        Chk(Filled(ArenaName(9999)) && Filled(ArenaClause(9999)), "ARENA out-of-range fallback is empty");

        // ── (4) FACTIONS — every enum member (incl. None) briefs and has a dossier with 3 parts.
        foreach (Faction fc in Enum.GetValues(typeof(Faction)))
        {
            Chk(Filled(FactionEpithet(fc)), $"FACTION {fc} empty epithet");
            Chk(Filled(FactionBriefLine(fc)), $"FACTION {fc} empty brief line");
            string dos = FactionDossier(fc);
            Chk(Filled(dos), $"FACTION {fc} empty dossier");
            Chk(dos.Split('\n').Length == 3, $"FACTION {fc} dossier is not 3 paragraphs");
            foreach (var para in dos.Split('\n')) Chk(Filled(para), $"FACTION {fc} dossier has an empty paragraph");
        }
        // A3 — THE NUMBERS MUST STAY TRUE. Each rewritten opposition line quotes the constant the
        // resolver actually applies; assert the interpolated value is literally present, so a
        // tuning change to any of them fails here instead of leaving the briefing lying.
        string synLine = FactionBriefLine(Faction.Syndicate);
        string legLine = FactionBriefLine(Faction.Legion);
        string wrdLine = FactionBriefLine(Faction.Wardens);
        Chk(synLine.Contains(LowCoverDefense.ToString()), $"SYNDICATE brief no longer states low cover's real defence ({LowCoverDefense}): {synLine}");
        Chk(legLine.Contains(Unit.CloseRange.ToString()), $"LEGION brief no longer states Unit.CloseRange ({Unit.CloseRange}): {legLine}");
        Chk(legLine.Contains("+" + Combat.LegionCloseAim), $"LEGION brief no longer states LegionCloseAim (+{Combat.LegionCloseAim}): {legLine}");
        Chk(legLine.Contains("+" + Combat.LegionCloseCrit), $"LEGION brief no longer states LegionCloseCrit (+{Combat.LegionCloseCrit}): {legLine}");
        Chk(wrdLine.Contains(Unit.LongRange.ToString()), $"WARDEN brief no longer states Unit.LongRange ({Unit.LongRange}): {wrdLine}");
        Chk(wrdLine.Contains("+" + Combat.WardenLongAim), $"WARDEN brief no longer states WardenLongAim (+{Combat.WardenLongAim}): {wrdLine}");
        // A3 — AND THEY MUST NOT BE THE SAME SENTENCE FOUR TIMES. C1's own review found three of
        // the four sharing one template ("<Faction> ground: a, b, c. <rule>."), which is what makes
        // the briefing card read as boilerplate by run four. Pin it: no line may use that shape,
        // and no two may open with the same word.
        var openers = new HashSet<string>();
        foreach (Faction fc in Enum.GetValues(typeof(Faction)))
        {
            string bl = FactionBriefLine(fc);
            Chk(!System.Text.RegularExpressions.Regex.IsMatch(bl, @"^\w+ ground: "),
                $"FACTION {fc} brief line fell back to the '<X> ground: ...' template: {bl}");
            string first = bl.Split(' ')[0].ToLowerInvariant();
            Chk(openers.Add(first), $"FACTION {fc} brief line opens on '{first}', already used by another faction");
        }

        // ── (5) BRIEFINGS — every objective x faction x arena (incl. procedural + finale) resolves
        //      to exactly 3 filled lines that fit the briefing card without wrapping past 3 rows.
        int briefMaxW = Hud.BriefBodyWidth;
        foreach (Objective ob in Enum.GetValues(typeof(Objective)))
        {
            Chk(Filled(ObjectiveLine(ob)), $"OBJECTIVE line empty for {ob}");
            Chk(Filled(BriefHead(ob)), $"BRIEF head empty for {ob}");
            foreach (Faction fc in Enum.GetValues(typeof(Faction)))
                for (int lay = -1; lay < Arenas.Length; lay++)
                {
                    var b = Brief(3, 987654321, lay, ob, fc, false, null, null);
                    Chk(b.Length == 3, $"BRIEF {ob}/{fc}/{lay} produced {b.Length} lines");
                    foreach (var ln in b)
                    {
                        Chk(Filled(ln), $"BRIEF {ob}/{fc}/{lay} has an empty/unfilled line");
                        Chk(Hud.WrapCount(ln, Hud.BriefFontSize, briefMaxW) <= 2,
                            $"BRIEF line wraps past 2 rows ({ob}/{fc}/{lay}): {ln}");
                    }
                }
        }
        foreach (Faction fc in new[] { Faction.Legion, Faction.Syndicate, Faction.Wardens, Faction.None })
        {
            var fb = Brief(Run.MaxMissions, 4242, 0, Objective.Decapitate, fc, true,
                           Run.FinaleBossName(fc), Run.FinaleKitClause(fc));
            foreach (var ln in fb)
            {
                Chk(Filled(ln), $"FINALE BRIEF empty line for {fc}");
                Chk(Hud.WrapCount(ln, Hud.BriefFontSize, briefMaxW) <= 2, $"FINALE BRIEF line wraps past 2 rows ({fc})");
            }
        }

        // ── (6) BARKS — every beat reachable, every variant fits the log, gates actually bite.
        string longest = "KESTREL";   // widest callsign in Mission.Callsigns (7 chars)
        foreach (Beat bt in Enum.GetValues(typeof(Beat)))
        {
            // A3 widened every pool from 3 to 6. Pinned at >= 6 so the depth cannot quietly
            // regress, and asserted DISTINCT so "widening" can never mean padding with repeats.
            Chk(Lines.ContainsKey(bt) && Lines[bt].Length >= 6, $"BEAT {bt} has fewer than 6 variants");
            var beatSeen = new HashSet<string>();
            foreach (var dup in Lines[bt]) Chk(beatSeen.Add(dup), $"BEAT {bt} lists the same line twice: {dup}");
            foreach (var raw in Lines[bt])
            {
                // the writing bar, as a check: a soldier on a radio does not shout in punctuation.
                Chk(!raw.Contains('!'), $"BEAT {bt} line uses an exclamation mark: {raw}");
                Chk(!string.IsNullOrWhiteSpace(raw), $"BEAT {bt} has an empty line");
                Chk(NeedsOther(bt) == raw.Contains("{0}"),
                    $"BEAT {bt} line slot mismatch (needs-other={NeedsOther(bt)}): {raw}");
                string body = NeedsOther(bt) ? raw.Replace("{0}", longest) : raw;
                string composed = Compose(longest, body);
                Chk(!composed.Contains("{0}"), $"BEAT {bt} left an unfilled slot: {composed}");
                float wpx = Cfg.Measure(composed, LogFontSize, 1f).X;
                Chk(wpx <= LogTextW, $"BARK overflows the log ({wpx:0}px > {LogTextW:0}px): {composed}");
            }
            // reachability: a fresh mission, a fresh speaker and a fresh turn must produce a line.
            BeginMission(11, 1);
            string spoken = TryBark(bt, "HAWK", NeedsOther(bt) ? "WREN" : null, 1);
            Chk(spoken != null, $"BEAT {bt} is unreachable through TryBark");
        }
        // a beat that needs a partner and is handed none must stay silent
        BeginMission(11, 1);
        Chk(TryBark(Beat.BondDown, "HAWK", null, 1) == null, "BondDown fired with no bond partner");
        Chk(TryBark(Beat.BondDown, "HAWK", "", 1) == null, "BondDown fired with an empty partner name");
        // gate 2: one bark per turn
        BeginMission(11, 1);
        Chk(TryBark(Beat.FirstBlood, "HAWK", null, 4) != null, "gate probe: first bark refused");
        Chk(TryBark(Beat.PodRout, "WREN", null, 4) == null, "GATE: two barks landed on the same turn");
        // gate 3: no repeat speaker
        BeginMission(11, 1);
        Chk(TryBark(Beat.FirstBlood, "HAWK", null, 1) != null, "gate probe: first bark refused");
        Chk(TryBark(Beat.PodRout, "HAWK", null, 2) == null, "GATE: the same speaker spoke twice in a row");
        Chk(TryBark(Beat.PodRout, "WREN", null, 3) != null, "GATE: a different speaker was wrongly refused");
        // gate 4: one bark per beat kind per mission
        BeginMission(11, 1);
        Chk(TryBark(Beat.PodRout, "HAWK", null, 1) != null, "gate probe: first rout bark refused");
        Chk(TryBark(Beat.PodRout, "WREN", null, 2) == null, "GATE: a beat kind fired twice in one mission");
        // BeginMission clears the budget
        BeginMission(11, 2);
        Chk(TryBark(Beat.PodRout, "HAWK", null, 1) != null, "BeginMission did not clear the bark budget");

        // ── (7) EPILOGUE — exactly 5 filled lines across the shapes a real run can end in,
        //      including the degenerate ones (nothing happened / everyone died / no memorial).
        var shapes = new List<RunFacts>();
        var mem3 = new List<FallenRec>
        {
            new FallenRec { Name = "ASH",  Cls = "RANGER",  Rank = "ROOKIE",   Kills = 0, Mission = 1 },
            new FallenRec { Name = "PIKE", Cls = "GUNNER",  Rank = "SERGEANT", Kills = 7, Mission = 4 },
            new FallenRec { Name = "MOTH", Cls = "CORPSMAN",Rank = "CORPORAL", Kills = 2, Mission = 4 },
        };
        var mem1 = new List<FallenRec>
        {
            new FallenRec { Name = "LARK", Cls = "ASSAULT", Rank = "", Kills = 0, Mission = 2 },
        };
        // topKills sweeps 0 / 1 (minority) / 2 (majority of 3) / 3 (all) / 9 (over-count, which the
        // caller can legitimately produce: DeathsByClass counts deaths, Memorial counts records, and
        // they are populated by different code paths) — every branch of line 4 has to stay true.
        foreach (bool win in new[] { true, false })
        foreach (int miss in new[] { 0, 1, 3, Run.MaxMissions })
        foreach (int surv in new[] { 0, 1, 4 })
        foreach (var mem in new List<FallenRec>[] { null, new List<FallenRec>(), mem1, mem3 })
        foreach (int topKills in new[] { 0, 1, 2, 3, 9 })
        foreach (int mvpK in new[] { 0, 1, 11 })
            shapes.Add(new RunFacts
            {
                Win = win, Missions = miss, MapSeed = 777 + miss, Heat = (miss % 3) * 4,
                Kills = miss * 5, Survivors = surv,
                MvpName = surv > 0 && mvpK > 0 ? "VESPER" : null, MvpKills = surv > 0 ? mvpK : 0,
                TopEnemyName = topKills > 0 ? "REAVER" : null, TopEnemyCls = topKills > 0 ? "BERSERKER" : null,
                TopEnemyKills = topKills,
                BossName = win ? "Siegelord" : null, Memorial = mem,
            });
        int epiMaxW = Hud.EpilogueWidth;
        foreach (var sh in shapes)
        {
            _epiKey = null;                      // defeat the memo so every shape really composes
            var e = Epilogue(sh);
            Chk(e.Count == 5, $"EPILOGUE produced {e.Count} lines (want 5) for win={sh.Win} m={sh.Missions}");
            foreach (var ln in e)
            {
                Chk(Filled(ln), $"EPILOGUE empty/unfilled line for win={sh.Win} m={sh.Missions}");
                Chk(!ln.Contains(" .") && !ln.Contains(" ,"), $"EPILOGUE dangling punctuation: {ln}");
                Chk(!ln.Contains("-1") && !ln.Contains(" 0 "), $"EPILOGUE leaked a degenerate number: {ln}");
                float w = Cfg.Measure(ln, Hud.EpilogueFontSize, 1f).X;
                Chk(w <= epiMaxW, $"EPILOGUE line overflows the card ({w:0}px > {epiMaxW}px): {ln}");
            }
        }
        // the memo returns the same object for the same facts, a fresh one when the facts move
        _epiKey = null;
        var e1 = Epilogue(shapes[0]);
        Chk(ReferenceEquals(e1, Epilogue(shapes[0])), "EPILOGUE memo recomposed for identical facts");
        var moved = shapes[0]; moved.Kills += 1;
        Chk(!ReferenceEquals(e1, Epilogue(moved)), "EPILOGUE memo returned a stale line set");

        // leave the bark state clean for whatever runs next in this process
        BeginMission(0, 0);
        if (fails.Count == 0) return "VOICETEST: PASS";
        return "VOICETEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────
    //  VOICEDUMP — SIGHTLINE_VOICEDUMP=1. Print a representative sample of EVERY text type to
    //  stdout, so the copy can be read and judged as prose without launching the game and
    //  walking six missions. Device-free, window-free, and it changes nothing: same pure
    //  generators the game calls. Purely a writing-review tool.
    // ─────────────────────────────────────────────────────────────────────────────────────
    public static string SampleReport()
    {
        var sb = new System.Text.StringBuilder();
        void H(string t) { sb.AppendLine(); sb.AppendLine("── " + t + " " + new string('─', Math.Max(0, 74 - t.Length))); }

        H("REGIONS (one campaign route per seed; six missions, six biomes, six names)");
        foreach (int seed in new[] { 4242, 99991, 7 })
        {
            var names = new List<string>();
            for (int m = 1; m <= Run.MaxMissions; m++)
                names.Add($"{Biome.All[Biome.IndexFor(m, seed)].Name}:{RegionName(m, seed)}");
            sb.AppendLine($"  seed {seed,-6} {string.Join("  >  ", names)}");
        }

        H("BRIEFINGS (region x arena x faction x objective)");
        var briefs = new (int m, int lay, Objective o, Faction f)[]
        {
            (1, 24, Objective.Eliminate,  Faction.None),
            (2,  4, Objective.Hack,       Faction.Wardens),
            (3,  1, Objective.Escort,     Faction.Syndicate),
            (4, 32, Objective.Sabotage,   Faction.Legion),
            (5,  8, Objective.Defend,     Faction.Wardens),
        };
        foreach (var b in briefs)
        {
            sb.AppendLine($"  [{BriefHead(b.o)}]   (arena {ArenaName(b.lay)})");
            foreach (var ln in Brief(b.m, 4242, b.lay, b.o, b.f, false, null, null)) sb.AppendLine("    " + ln);
            sb.AppendLine();
        }
        foreach (var f in new[] { Faction.Legion, Faction.Syndicate, Faction.Wardens })
        {
            sb.AppendLine($"  [FINALE / {FactionEpithet(f)}]");
            foreach (var ln in Brief(Run.MaxMissions, 4242, 11, Objective.Decapitate, f, true,
                                     Run.FinaleBossName(f), Run.FinaleKitClause(f))) sb.AppendLine("    " + ln);
            sb.AppendLine();
        }

        H("FACTION DOSSIERS (codex FACTIONS tab)");
        foreach (var f in new[] { Faction.Syndicate, Faction.Legion, Faction.Wardens, Faction.None })
        {
            sb.AppendLine($"  {FactionEpithet(f)}");
            foreach (var para in FactionDossier(f).Split('\n')) sb.AppendLine("    " + para);
            sb.AppendLine();
        }

        H("BARKS (every beat, every variant, as the combat log prints them)");
        foreach (Beat bt in Enum.GetValues(typeof(Beat)))
        {
            sb.AppendLine($"  {bt}:");
            foreach (var raw in Lines[bt])
            {
                string body = NeedsOther(bt) ? raw.Replace("{0}", "WREN") : raw;
                sb.AppendLine("    " + Compose("KESTREL", body));
            }
        }

        H("EPILOGUES");
        var mem = new List<FallenRec>
        {
            new FallenRec { Name = "DALES \"BISHOP\"", Cls = "RANGER",  Rank = "SERGEANT", Kills = 7, Mission = 2 },
            new FallenRec { Name = "OKONKWO",        Cls = "GUNNER",  Rank = "CORPORAL", Kills = 4, Mission = 4 },
            new FallenRec { Name = "VEGA \"ASH\"",    Cls = "ASSAULT", Rank = "ROOKIE",   Kills = 1, Mission = 5 },
        };
        var cases = new (string label, RunFacts f)[]
        {
            ("WIN, three lost", new RunFacts { Win = true, Missions = Run.MaxMissions, MapSeed = 4242, Heat = 3,
                Kills = 47, Survivors = 4, MvpName = "VEGA", MvpKills = 11, TopEnemyName = "REAVER",
                TopEnemyCls = "BERSERKER", TopEnemyKills = 2, BossName = "Spymaster", Memorial = mem }),
            ("WIN, flawless", new RunFacts { Win = true, Missions = Run.MaxMissions, MapSeed = 4242, Heat = 0,
                Kills = 52, Survivors = 6, MvpName = "LARK", MvpKills = 14, BossName = "Siegelord",
                Memorial = new List<FallenRec>() }),
            ("LOSS on m5", new RunFacts { Win = false, Missions = 4, MapSeed = 4242, Heat = 3, Kills = 47,
                Survivors = 4, MvpName = "VEGA", MvpKills = 11, TopEnemyName = "REAVER",
                TopEnemyCls = "BERSERKER", TopEnemyKills = 2, Memorial = mem }),
            ("WIPE on m2", new RunFacts { Win = false, Missions = 1, MapSeed = 99991, Heat = 6, Kills = 6,
                Survivors = 0, TopEnemyName = "VIPER", TopEnemyCls = "SNIPER", TopEnemyKills = 4,
                Memorial = new List<FallenRec>
                {
                    new FallenRec { Name = "MICA", Cls = "CORPSMAN", Rank = "ROOKIE", Kills = 0, Mission = 2 },
                    new FallenRec { Name = "RUNE", Cls = "GUNNER",   Rank = "ROOKIE", Kills = 2, Mission = 2 },
                    new FallenRec { Name = "TALON",Cls = "RANGER",   Rank = "ROOKIE", Kills = 1, Mission = 2 },
                    new FallenRec { Name = "GALE", Cls = "ASSAULT",  Rank = "ROOKIE", Kills = 3, Mission = 2 },
                } }),
        };
        foreach (var c in cases)
        {
            sb.AppendLine($"  [{c.label}]");
            _epiKey = null;
            foreach (var ln in Epilogue(c.f)) sb.AppendLine("    " + ln);
            sb.AppendLine();
        }
        _epiKey = null;
        BeginMission(0, 0);
        return sb.ToString();
    }

    static bool SeqEq(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// Run every generator in this file once, over a spread of inputs. Used by the RNG-separation
    /// probe: whatever this touches must not move the shared stream.
    static void ExerciseEverything()
    {
        BeginMission(4242, 3);
        for (int m = 1; m <= Run.MaxMissions; m++) RegionName(m, 4242);
        for (int i = -1; i < Arenas.Length; i++) { ArenaName(i); ArenaClause(i); }
        foreach (Faction fc in Enum.GetValues(typeof(Faction)))
        { FactionEpithet(fc); FactionBriefLine(fc); FactionDossier(fc); }
        foreach (Objective ob in Enum.GetValues(typeof(Objective)))
        { ObjectiveLine(ob); BriefHead(ob); Brief(2, 4242, 5, ob, Faction.Legion, false, null, null); }
        Brief(6, 4242, 0, Objective.Decapitate, Faction.Wardens, true,
              Run.FinaleBossName(Faction.Wardens), Run.FinaleKitClause(Faction.Wardens));
        foreach (Beat bt in Enum.GetValues(typeof(Beat)))
            TryBark(bt, "HAWK" + (int)bt, NeedsOther(bt) ? "WREN" : null, (int)bt);
        _epiKey = null;
        Epilogue(new RunFacts
        {
            Win = false, Missions = 3, MapSeed = 4242, Heat = 4, Kills = 21, Survivors = 2,
            MvpName = "LARK", MvpKills = 9, TopEnemyName = "VIPER", TopEnemyCls = "SNIPER", TopEnemyKills = 2,
            Memorial = new List<FallenRec> { new FallenRec { Name = "ONYX", Cls = "ASSAULT", Rank = "CORPORAL", Kills = 4, Mission = 2 } },
        });
    }
}
