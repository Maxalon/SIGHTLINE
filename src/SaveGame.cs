using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sightline;

/// Run persistence: serialise the campaign Run (squad incl. perks/weapon/rank +
/// mission number + the active deployment card) to the OS user-data dir, NOT the
/// repo. A run is checkpointed at the start of each mission; CONTINUE on the intro
/// reloads it and resumes that mission from its start. The save is deleted when a
/// run ends (win or wipe). Compact DTOs keep only persistent fields; transient
/// per-mission state (ammo/grenades/ability/position) is rebuilt by Mission.Build.
public static class SaveGame
{
    // ~/.local/share/Sightline (Linux) / %AppData%/Sightline (Windows) / ~/Library/... (mac)
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sightline");
    static string FilePath => Path.Combine(Dir, "save.json");
    static string MetaPath => Path.Combine(Dir, "meta.json");

    static readonly JsonSerializerOptions Opts = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };

    public static bool Exists
    {
        get { try { return File.Exists(FilePath); } catch { return false; } }
    }

    /// Test-only path exposure (MODETEST abandon leg preserves/restores any real save.json,
    /// mirroring MetaPathPublic). Not used by gameplay code.
    public static string SavePathPublic => FilePath;

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best effort */ }
    }

    public static void Save(Run run)
    {
        if (run == null) return;
        try
        {
            // Atomic write: serialize to a sibling .tmp then rename over the target
            // (File.Move w/ overwrite is rename(2) on the same volume), so a crash or
            // torn write mid-save can never leave a half-written save.json behind.
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(ToDto(run), Opts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* a failed save must never crash the game */ }
    }

    /// Load the saved run, or null if there is none / it is unreadable (an unreadable
    /// file is moved aside to save.json.bak — evidence preserved, and the intro stops
    /// offering a broken CONTINUE because save.json itself is gone).
    public static Run Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var dto = JsonSerializer.Deserialize<RunDto>(File.ReadAllText(FilePath), Opts);
            return dto == null ? null : FromDto(dto);
        }
        catch
        {
            // Never destroy evidence: stash the unreadable save instead of deleting it.
            // Recovery I/O must never crash (we're already inside the failure path).
            try { if (File.Exists(FilePath)) File.Move(FilePath, FilePath + ".bak", overwrite: true); }
            catch { }
            return null;
        }
    }

    // ---- meta persistence (Heat/Ascension unlock) ----
    // The max-unlocked Heat is META: it survives run end (unlike save.json, which is deleted
    // when a run ends). Stored in its own tiny meta.json. Gated by Game.NoPersist at the call
    // sites exactly like the run save, so the harness never touches disk.
    // meta.json carries several independent fields (MaxHeat, LossStreak). Always read-modify-write
    // the whole DTO so saving one field never clobbers another. Missing fields default to 0, so an
    // old meta.json (heat-only) still loads — append-only and forward-compatible.
    // Set once a corrupt meta.json has been stashed to meta.json.bak this session, so a
    // later corrupt read can never overwrite that evidence with a fresher corpse.
    static bool _metaEvidenceStashed;

    static MetaDto LoadMetaDto()
    {
        try { if (File.Exists(MetaPath)) return JsonSerializer.Deserialize<MetaDto>(File.ReadAllText(MetaPath), Opts) ?? new MetaDto(); }
        catch
        {
            // Never destroy evidence: an unreadable meta.json used to yield a fresh
            // MetaDto whose next read-modify-write silently overwrote the whole profile
            // (veterans, salvage, achievements, hall of fame). Stash the corrupt bytes as
            // meta.json.bak first (overwriting a stale .bak from an older session, but
            // never one stashed earlier THIS session). Recovery I/O must never crash —
            // swallow its own failures too.
            try
            {
                if (!_metaEvidenceStashed && File.Exists(MetaPath))
                {
                    File.Copy(MetaPath, MetaPath + ".bak", true);
                    _metaEvidenceStashed = true;
                }
            }
            catch { }
        }
        return new MetaDto();
    }

    static void WriteMetaDto(MetaDto dto)
    {
        try
        {
            // Atomic write (same pattern as Save): .tmp then rename, so the game's only
            // permanent state can't be torn by a crash mid-write.
            Directory.CreateDirectory(Dir);
            string tmp = MetaPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, Opts));
            File.Move(tmp, MetaPath, overwrite: true);
        }
        catch { /* a failed meta save must never crash the game */ }
    }

    public static int LoadMetaHeat() => Heat.Clamp(LoadMetaDto().MaxHeat);

    public static void SaveMetaHeat(int maxHeat)
    {
        var d = LoadMetaDto(); d.MaxHeat = Heat.Clamp(maxHeat); WriteMetaDto(d);
    }

    /// Adaptive-assist meta: how many runs the player has lost in a row (0 on a fresh profile).
    public static int LoadMetaLossStreak() => Math.Max(0, LoadMetaDto().LossStreak);

    public static void SaveMetaLossStreak(int streak)
    {
        var d = LoadMetaDto(); d.LossStreak = Math.Max(0, streak); WriteMetaDto(d);
    }

    /// PROGRAM HORIZON W2 (LAST STAND): the best endless wave ever reached, persisted across
    /// sessions in the shared meta.json (append-only, whole-DTO read-modify-write so it never
    /// clobbers MaxHeat/LossStreak). 0 on a fresh profile.
    public static int LoadMetaBestWave() => Math.Max(0, LoadMetaDto().BestWave);

    public static void SaveMetaBestWave(int wave)
    {
        var d = LoadMetaDto(); d.BestWave = Math.Max(0, wave); WriteMetaDto(d);
    }

    /// PROGRAM HORIZON W4 (SEEDED DAILY): the persisted best for a given day's challenge. Stored as a
    /// (stamp, best) pair — a NEW day (different stamp) reads 0 (unplayed today). "Best" is the fewest
    /// turns to a WIN (lower is better; 0 = not yet cleared). Append-only, whole-DTO read-modify-write
    /// so it never clobbers MaxHeat/LossStreak/BestWave/salvage/etc. Gated by NoPersist at the call sites.
    public static int LoadDailyBest(int stamp)
    {
        var d = LoadMetaDto();
        return d.DailyStamp == stamp ? Math.Max(0, d.DailyBest) : 0;   // a different/older day = unplayed
    }

    public static void SaveDailyResult(int stamp, int best)
    {
        var d = LoadMetaDto();
        d.DailyStamp = stamp; d.DailyBest = Math.Max(0, best); WriteMetaDto(d);
    }

    // ---- W9 (SIGNAL): daily WIN payout + streak ----
    // A daily win pays a salvage bounty ONCE per stamp (keyed on DailyWinStamp, so replaying the
    // same day's challenge can never farm it), and drives a consecutive-day WIN streak counter.
    // Whole-DTO read-modify-write like every other meta field; NoPersist-gated at the call site.

    /// The current consecutive-day daily-win streak (0 on a fresh profile).
    public static int LoadDailyStreak() => Math.Max(0, LoadMetaDto().DailyStreak);

    /// Record a daily WIN for `stamp`. Pays out at most once per stamp: returns (paid=false) if this
    /// stamp already paid. The streak increments when `stamp` is the calendar day AFTER the last paid
    /// win; any gap (or a fresh profile) resets it to 1. Returns the updated streak either way.
    public static (bool paid, int streak) RecordDailyWin(int stamp)
    {
        var d = LoadMetaDto();
        if (d.DailyWinStamp == stamp) return (false, Math.Max(0, d.DailyStreak));   // already paid today
        d.DailyStreak = IsNextDay(d.DailyWinStamp, stamp) ? Math.Max(0, d.DailyStreak) + 1 : 1;
        d.DailyWinStamp = stamp;
        WriteMetaDto(d);
        return (true, d.DailyStreak);
    }

    /// True when yyyymmdd stamp `cur` is exactly the calendar day after `prev` (false on any parse
    /// failure or a fresh profile's 0 — recovery must never crash the payout path).
    static bool IsNextDay(int prev, int cur)
    {
        if (prev <= 0) return false;
        try
        {
            var p = new DateTime(prev / 10000, prev / 100 % 100, prev % 100);
            var c = new DateTime(cur / 10000, cur / 100 % 100, cur % 100);
            return (c - p).Days == 1;
        }
        catch { return false; }
    }

    // ---- PROGRAM HORIZON W3 (WAR ROOM): cross-run meta-progression ----
    // A persistent SALVAGE currency + ACHIEVEMENTS + additive UNLOCKS + a HALL OF FAME (Legends) +
    // lifetime run totals, all in the shared meta.json (append-only, whole-DTO read-modify-write so a
    // write never clobbers MaxHeat/LossStreak/BestWave). Gated by Game.NoPersist at the CALL sites, so
    // the flywheel/harness never touch these => balance stays byte-stable. Fresh profile = all defaults.

    /// Persistent SALVAGE currency (0 on a fresh profile).
    public static int LoadSalvage() => Math.Max(0, LoadMetaDto().Salvage);

    /// Add to the SALVAGE bank (clamped >= 0). No-op for non-positive amounts.
    public static void AddSalvage(int amount)
    {
        if (amount <= 0) return;
        var d = LoadMetaDto(); d.Salvage = Math.Max(0, d.Salvage) + amount; WriteMetaDto(d);
    }

    /// Spend SALVAGE; returns false (and spends nothing) if the bank is insufficient.
    public static bool SpendSalvage(int cost)
    {
        if (cost <= 0) return true;
        var d = LoadMetaDto();
        if (d.Salvage < cost) return false;
        d.Salvage -= cost; WriteMetaDto(d);
        return true;
    }

    /// The unlocked achievement ids (empty on a fresh profile).
    public static List<string> LoadAchievements() => LoadMetaDto().Achievements ?? new List<string>();

    /// Unlock an achievement by id; returns true if it was NEWLY unlocked (false if already had).
    public static bool UnlockAchievement(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var d = LoadMetaDto();
        d.Achievements ??= new List<string>();
        if (d.Achievements.Contains(id)) return false;
        d.Achievements.Add(id); WriteMetaDto(d);
        return true;
    }

    /// The purchased meta-unlock ordinals (empty on a fresh profile).
    public static List<int> LoadUnlocks() => LoadMetaDto().Unlocks ?? new List<int>();

    /// Grant a meta-unlock (idempotent).
    public static void AddUnlock(int unlock)
    {
        var d = LoadMetaDto();
        d.Unlocks ??= new List<int>();
        if (d.Unlocks.Contains(unlock)) return;
        d.Unlocks.Add(unlock); WriteMetaDto(d);
    }

    /// Whether a meta-unlock has been purchased.
    public static bool HasUnlock(int unlock)
    {
        var u = LoadMetaDto().Unlocks;
        return u != null && u.Contains(unlock);
    }

    /// The HALL OF FAME legends, most-recent first (empty on a fresh profile).
    public static List<LegendDto> LoadLegends() => LoadMetaDto().Legends ?? new List<LegendDto>();

    /// Prepend legends (most-recent first) and cap the stored history at 40.
    public static void AddLegends(IEnumerable<LegendDto> legends)
    {
        if (legends == null) return;
        var add = new List<LegendDto>(legends);
        if (add.Count == 0) return;
        var d = LoadMetaDto();
        d.Legends ??= new List<LegendDto>();
        // newest first: the just-ended run's entries lead the list
        d.Legends.InsertRange(0, add);
        if (d.Legends.Count > 40) d.Legends.RemoveRange(40, d.Legends.Count - 40);
        WriteMetaDto(d);
    }

    /// Lifetime run totals: (runs started+finished, wins, best missions reached).
    public static (int runs, int wins, int best) LoadRunTotals()
    {
        var d = LoadMetaDto();
        return (Math.Max(0, d.TotalRuns), Math.Max(0, d.TotalWins), Math.Max(0, d.BestMissions));
    }

    /// Record a finished run: increments totals, wins on a victory, and tracks the deepest mission.
    public static void RecordRunTotals(bool win, int missions)
    {
        var d = LoadMetaDto();
        d.TotalRuns = Math.Max(0, d.TotalRuns) + 1;
        if (win) d.TotalWins = Math.Max(0, d.TotalWins) + 1;
        d.BestMissions = Math.Max(Math.Max(0, d.BestMissions), missions);
        WriteMetaDto(d);
    }

    // append-only: new fields default to 0 / null, so an old meta.json (heat/streak/bestwave only)
    // still loads. Lists default null -> the accessors coalesce to empty (never NRE).
    class MetaDto
    {
        public int MaxHeat; public int LossStreak; public int BestWave;
        // W3 WAR ROOM (all append-only):
        public int Salvage;
        public List<string> Achievements;
        public List<int> Unlocks;
        public List<LegendDto> Legends;
        public int TotalRuns, TotalWins, BestMissions;
        // W4 SEEDED DAILY (append-only): the last-played day (yyyymmdd) + its best (fewest win-turns; 0 = uncleared).
        public int DailyStamp, DailyBest;
        // COUNTERPLAY (append-only): the cross-run VETERAN reserve — promoted survivors of finished runs,
        // recallable in a future run's draft. Old profiles have no list -> null -> empty (inert).
        public List<UnitDto> Veterans;
        // W9 SIGNAL (append-only): the last daily stamp that PAID its win bounty (unfarmable key) +
        // the consecutive-day daily-win streak. Old profiles default 0/0 (no streak, nothing paid).
        public int DailyWinStamp, DailyStreak;
    }

    /// A HALL OF FAME entry (WAR ROOM): a soldier snapshot at run end — a fallen KIA (Won=false) or a
    /// survivor of a WON run (Won=true). Public so Game/Hud can build + read them. Persisted in meta.json.
    public class LegendDto
    {
        public string Name, Cls, Rank;
        public int Kills, Heat;
        public bool Won;
    }

    /// Test-only accessor to the meta.json path (used by Game.HordeSelfTest to preserve/restore any
    /// real meta while it round-trips BestWave). Not for gameplay use.
    public static string MetaPathPublic => MetaPath;

    // ---- mapping ----
    // A single Unit <-> UnitDto mapping, reused by the run save (ToDto/FromDto) AND the cross-run
    // VETERAN reserve (EnshrineVeterans/LoadVeterans), so both persist the identical persistent
    // field set. Transient per-mission state (ammo/pos/grenades/statuses) is intentionally excluded —
    // it is rebuilt by Mission.Build on deploy.
    static UnitDto ToUnitDto(Unit u) => new UnitDto
    {
        Name = u.Name, Cls = u.Cls,
        Hp = u.Hp, MaxHp = u.MaxHp, Aim = u.Aim, Mobility = u.Mobility,
        Weapon = (int)u.Weapon.Kind, Kills = u.Kills, Rank = u.Rank,
        BonusGrenades = u.BonusGrenades,
        CustomTag = u.CustomTag,
        Wound = u.Wound,
        Armor = u.Armor,
        Benched = u.Benched,
        Perks = u.Perks.ConvertAll(p => (int)p),
        WeaponMods = u.WeaponMods.ConvertAll(m => (int)m),
        Nickname = u.Nickname,
        Traits = u.Traits.ConvertAll(t => (int)t),
        Bonds = new List<string>(u.Bonds),
        Spec = (int)u.Spec,
        Scars = u.Scars.ConvertAll(s => (int)s),
        VendettaFaction = (int)u.VendettaFaction,
        NearDeathCount = u.NearDeathCount,
    };

    /// Rebuild a Unit from a persisted UnitDto (Team.Player, Alive, weapon mods re-baked, ammo seeded).
    /// Shared by the run load and the veteran reserve. `fromReserve` tags a returning veteran for the
    /// draft-screen display (transient, never persisted).
    static Unit FromUnitDto(UnitDto d, bool fromReserve = false)
    {
        var u = new Unit
        {
            Name = d.Name, Cls = d.Cls, Team = Team.Player,
            Hp = d.Hp, MaxHp = d.MaxHp, Aim = d.Aim, Mobility = d.Mobility,
            Weapon = Weapon.Make((WeaponKind)d.Weapon),
            Kills = d.Kills, Rank = d.Rank, Alive = true,
            BonusGrenades = d.BonusGrenades, CustomTag = d.CustomTag, Wound = d.Wound,
            Armor = d.Armor,
            Nickname = d.Nickname, Benched = d.Benched,
            Spec = (Spec)d.Spec,
            FromReserve = fromReserve,
        };
        if (d.Perks != null) foreach (var p in d.Perks) u.Perks.Add((Perk)p);
        if (d.WeaponMods != null) foreach (var m in d.WeaponMods) u.WeaponMods.Add((WeaponMod)m);
        u.RefreshWeaponMods();
        u.Ammo = u.Weapon.Clip;
        if (d.Traits != null) foreach (var t in d.Traits) u.Traits.Add((Trait)t);
        if (d.Bonds != null) u.Bonds = new List<string>(d.Bonds);
        if (d.Scars != null) foreach (var s in d.Scars) u.Scars.Add((Scar)s);
        u.VendettaFaction = (Faction)d.VendettaFaction;
        u.NearDeathCount = d.NearDeathCount;
        return u;
    }

    // ---- cross-run VETERAN reserve (persisted in meta.json, append-only) ----
    // Soldiers who distinguished themselves (promoted survivors of a finished run) retire into a
    // persistent reserve the next run's DRAFT can recall — carrying their rank/perks/traits/spec/scars.
    // Whole-DTO read-modify-write like every other meta field; NoPersist-gated at the call sites so the
    // flywheel/harness never read or write it (byte-stable).
    public const int MaxVeterans = 12;   // reserve cap; least-storied are dropped when it overflows

    /// The recruitable veteran reserve, most-storied first (empty on a fresh profile). A recalled veteran
    /// arrives FRESH for the new campaign — full HP and no carried wound (between-run downtime); their
    /// earned rank/perks/traits/spec/scars carry over. (Mission.Build also re-heals on deploy, so this is
    /// belt-and-suspenders, but it makes the draft card's HP read truthful.)
    public static List<Unit> LoadVeterans()
    {
        var dtos = LoadMetaDto().Veterans;
        var list = new List<Unit>();
        if (dtos != null)
            foreach (var d in dtos)
            {
                var u = FromUnitDto(d, fromReserve: true);
                u.Hp = u.MaxHp; u.Wound = 0;
                list.Add(u);
            }
        return list;
    }

    public static int VeteranCount() => LoadMetaDto().Veterans?.Count ?? 0;

    /// Retire the given survivors into the reserve: snapshot each, dedupe by name (keep the newer,
    /// more-storied record), then cap to MaxVeterans keeping the most-storied. Idempotent per name.
    public static void EnshrineVeterans(IEnumerable<Unit> vets)
    {
        if (vets == null) return;
        var add = new List<Unit>(vets);
        if (add.Count == 0) return;
        var d = LoadMetaDto();
        d.Veterans ??= new List<UnitDto>();
        foreach (var v in add)
        {
            if (v == null || string.IsNullOrEmpty(v.Name)) continue;
            d.Veterans.RemoveAll(e => e.Name == v.Name);   // newest record wins for a returning name
            d.Veterans.Add(ToUnitDto(v));
        }
        // keep the most-storied (kills, then rank) when over the cap
        d.Veterans.Sort((x, y) => (y.Kills * 4 + y.Rank).CompareTo(x.Kills * 4 + x.Rank));
        if (d.Veterans.Count > MaxVeterans) d.Veterans.RemoveRange(MaxVeterans, d.Veterans.Count - MaxVeterans);
        WriteMetaDto(d);
    }

    static RunDto ToDto(Run r)
    {
        var dto = new RunDto
        {
            Mission = r.Mission, Intel = r.Intel, Fallen = new List<string>(r.Fallen),
            BondTally = new Dictionary<string, int>(r.BondTally),
            MapSeed = r.MapSeed, MapPos = r.MapPos,
            HeatLevel = r.HeatLevel,
            ActiveBoons = r.ActiveBoons.ConvertAll(b => (int)b),
            PrepFaction = (int)r.PrepFaction,
            CheckpointUsed = r.CheckpointUsed,
            Contract = (int)r.Contract,
        };
        foreach (var u in r.Squad)
            dto.Squad.Add(ToUnitDto(u));
        var c = r.CurrentCard;
        if (c != null)
            dto.Card = new CardDto
            {
                Objective = (int)c.Objective, ModName = c.ModName,
                EnemyDelta = c.EnemyDelta, StatDelta = c.StatDelta,
                Reward = (int)c.Reward, RewardText = c.RewardText,
            };
        return dto;
    }

    static Run FromDto(RunDto dto)
    {
        var r = new Run { Mission = dto.Mission, Intel = dto.Intel, Squad = new List<Unit>(), HeatLevel = Heat.Clamp(dto.HeatLevel) };
        if (dto.Fallen != null) r.Fallen = new List<string>(dto.Fallen);
        if (dto.BondTally != null) r.BondTally = new Dictionary<string, int>(dto.BondTally);
        if (dto.ActiveBoons != null) foreach (var b in dto.ActiveBoons) r.ActiveBoons.Add((Boon)b);
        r.PrepFaction = (Faction)dto.PrepFaction;   // append-only: old saves default 0 == Faction.None
        r.CheckpointUsed = dto.CheckpointUsed;      // append-only: old saves default false
        r.Contract = (Contract)dto.Contract;        // append-only: old saves default 0 == Contract.None
        // regenerate the branching campaign map from its seed and restore the position
        if (dto.MapSeed != 0)
        {
            r.MapSeed = dto.MapSeed;
            r.GenerateMap(dto.MapSeed);
            r.MapPos = (dto.MapPos >= 0 && dto.MapPos < r.Map.Count) ? dto.MapPos : 0;
            if (r.CurrentNode != null) r.CurrentNode.Visited = true;
        }
        // installed weapon mods are re-baked BEFORE ammo seeding inside FromUnitDto so an EXTENDED MAG
        // is reflected in the starting clip; all append-only fields default inert for old saves.
        foreach (var d in dto.Squad)
            r.Squad.Add(FromUnitDto(d));
        var cd = dto.Card;
        r.CurrentCard = cd == null
            ? Run.StandardCard(Math.Max(1, dto.Mission))
            : new MissionCard
            {
                Objective = (Objective)cd.Objective, ModName = cd.ModName,
                EnemyDelta = cd.EnemyDelta, StatDelta = cd.StatDelta,
                Reward = (RewardKind)cd.Reward, RewardText = cd.RewardText,
            };
        return r;
    }

    // ---- DTOs (public fields, IncludeFields = true) ----
    class RunDto
    {
        public int Mission;
        public int Intel;
        public List<UnitDto> Squad = new();
        public List<string> Fallen = new();
        public Dictionary<string, int> BondTally = new();
        public CardDto Card;
        public int MapSeed;
        public int MapPos;
        public int HeatLevel;   // append-only: chosen Heat/Ascension level (old saves default 0)
        public List<int> ActiveBoons = new();   // append-only: run-scoped boons (old saves default empty)
        public int PrepFaction;   // append-only: faction COUNTER-PREP bought (old saves default 0 == None)
        public bool CheckpointUsed;   // append-only: the one-time REINFORCEMENTS redeploy spent (old saves default false)
        public int Contract;   // append-only: W6 run contract (old saves default 0 == Contract.None)
    }

    class UnitDto
    {
        public string Name, Cls, CustomTag, Nickname;
        public int Hp, MaxHp, Aim, Mobility, Weapon, Kills, Rank, BonusGrenades, Wound, Armor;
        public bool Benched;
        public List<int> Perks = new();
        public List<int> WeaponMods = new();   // append-only: persisted weapon upgrades (old saves default empty)
        public List<int> Traits = new();
        public List<string> Bonds = new();
        public int Spec;   // append-only: class specialization fork (old saves default 0 == Spec.None)
        public List<int> Scars = new();   // append-only: W5 trauma scars (old saves default empty)
        public int VendettaFaction;       // append-only: faction that scarred this soldier (old saves default 0 == None)
        public int NearDeathCount;        // append-only: survived near-deaths (old saves default 0)
    }

    class CardDto
    {
        public int Objective;
        public string ModName;
        public int EnemyDelta, StatDelta, Reward;
        public string RewardText;
    }

    /// Headless self-test (SIGHTLINE_SAVETEST): round-trip a populated run through
    /// disk and confirm the persistent fields survive. Returns a one-line report.
    public static string SelfTest()
    {
        string saved = Exists ? File.ReadAllText(FilePath) : null;  // preserve any real save
        try
        {
            var src = new Run { Mission = 4, Intel = 23, Squad = new List<Unit>(), HeatLevel = 5 };
            src.Fallen.Add("DOWNED-GUY");
            src.PrepFaction = Faction.Legion;   // a staged faction counter-prep must round-trip
            src.CheckpointUsed = true;          // the one-time REINFORCEMENTS flag must round-trip
            src.Contract = Contract.HighStakes; // a chosen run contract (W6) must round-trip
            var a = new Unit
            {
                Name = "VEGA", Cls = "ASSAULT", Team = Team.Player,
                Hp = 4, MaxHp = 11, Aim = 78, Mobility = 8,
                Weapon = Weapon.Make(WeaponKind.Rifle), Kills = 7, Rank = 3, BonusGrenades = 2,
                CustomTag = "BREACHER", Wound = 2,
            };
            a.Benched = true;
            // ARMORY: a player-chosen weapon (ASSAULT re-armed Rifle -> Shotgun). Must round-trip,
            // and the installed mods must re-bake onto the SWAPPED weapon.
            a.Weapon = Weapon.Make(WeaponKind.Shotgun);
            a.Perks.Add(Perk.Deadeye); a.Perks.Add(Perk.Tank); a.Perks.Add(Perk.Vantage);   // incl. a HORIZON-w6 perk -> round-trips by ordinal
            a.InstallMod(WeaponMod.Scope); a.InstallMod(WeaponMod.ExtendedMag);   // persistent weapon upgrades
            a.Nickname = "REAPER";
            a.Traits.Add(Trait.Killer); a.Traits.Add(Trait.IronWill);
            a.Bonds.Add("NOX");
            a.Spec = Spec.Breacher;   // a chosen class specialization fork must round-trip
            // W5 SCARS: earned trauma identity must round-trip (scars by ordinal + vendetta + count)
            a.Scars.Add(Scar.ShellShocked); a.Scars.Add(Scar.Vendetta);
            a.VendettaFaction = Faction.Wardens;
            a.NearDeathCount = 2;
            src.Squad.Add(a);
            var n = new Unit { Name = "NOX", Cls = "SHARPSHOOTER", Team = Team.Player, Hp = 6, MaxHp = 6, Aim = 76, Mobility = 6, Weapon = Weapon.Make(WeaponKind.Sniper), Kills = 2, Rank = 1 };
            n.Bonds.Add("VEGA");
            src.Squad.Add(n);
            src.BondTally[Run.BondKey("VEGA", "NOX")] = 3;
            src.GenerateMap(424242);
            src.MapSeed = 424242;
            src.JumpTo(3);   // advance the map position a few columns
            int srcPos = src.MapPos;
            src.CurrentCard = new MissionCard { Objective = Objective.Hack, ModName = "ONSLAUGHT", EnemyDelta = 2, StatDelta = 1, Reward = RewardKind.BonusPerk, RewardText = "Bonus perk" };

            Save(src);
            var got = Load();
            if (got == null) return "SAVETEST: FAIL (load returned null)";

            var fails = new List<string>();
            if (got.Mission != src.Mission) fails.Add("mission");
            if (got.Intel != src.Intel) fails.Add("intel");
            if (got.Squad.Count != src.Squad.Count) fails.Add("squadCount");
            if (got.Fallen.Count != 1 || got.Fallen[0] != "DOWNED-GUY") fails.Add("fallen");
            var g0 = got.Squad[0];
            if (g0.Name != a.Name || g0.Cls != a.Cls || g0.Hp != a.Hp || g0.MaxHp != a.MaxHp ||
                g0.Aim != a.Aim || g0.Mobility != a.Mobility || g0.Kills != a.Kills || g0.Rank != a.Rank)
                fails.Add("unit0Stats");
            if (g0.Weapon.Kind != WeaponKind.Shotgun) fails.Add("weapon");   // ARMORY re-arm persists
            if (g0.BonusGrenades != 2) fails.Add("bonusGrenades");
            if (g0.CustomTag != "BREACHER") fails.Add("customTag");
            if (g0.Wound != 2) fails.Add("wound");
            if (!g0.Benched) fails.Add("benched");
            if (!g0.HasPerk(Perk.Deadeye) || !g0.HasPerk(Perk.Tank) || !g0.HasPerk(Perk.Vantage) || g0.Perks.Count != 3) fails.Add("perks");
            // weapon mods round-trip AND re-bake onto the rebuilt weapon's effective stats
            if (!g0.HasMod(WeaponMod.Scope) || !g0.HasMod(WeaponMod.ExtendedMag) || g0.WeaponMods.Count != 2) fails.Add("weaponMods");
            if (g0.Weapon.AimBonus != WeaponModDef.ScopeAim) fails.Add("weaponModScopeApplied");      // Shotgun base aimBonus 0 + scope
            if (g0.Weapon.Clip != 2 + WeaponModDef.MagClip) fails.Add("weaponModMagApplied");         // Shotgun base clip 2 + extended mag
            if (g0.Nickname != "REAPER") fails.Add("nickname");
            if (!g0.HasTrait(Trait.Killer) || !g0.HasTrait(Trait.IronWill) || g0.Traits.Count != 2) fails.Add("traits");
            if (g0.Bonds.Count != 1 || g0.Bonds[0] != "NOX") fails.Add("bonds");
            if (g0.Spec != Spec.Breacher) fails.Add("spec");
            // W5 SCARS round-trip
            if (!g0.HasScar(Scar.ShellShocked) || !g0.HasScar(Scar.Vendetta) || g0.Scars.Count != 2) fails.Add("scars");
            if (g0.VendettaFaction != Faction.Wardens) fails.Add("vendettaFaction");
            if (g0.NearDeathCount != 2) fails.Add("nearDeathCount");
            if (!got.BondTally.TryGetValue(Run.BondKey("VEGA", "NOX"), out var bt) || bt != 3) fails.Add("bondTally");
            if (got.CurrentCard == null || got.CurrentCard.Objective != Objective.Hack ||
                got.CurrentCard.EnemyDelta != 2 || got.CurrentCard.Reward != RewardKind.BonusPerk)
                fails.Add("card");
            if (got.MapSeed != 424242) fails.Add("mapSeed");
            if (got.Map.Count == 0) fails.Add("mapRegen");
            if (got.MapPos != srcPos) fails.Add("mapPos");
            if (got.CurrentNode == null || got.CurrentNode.Mission != 3) fails.Add("mapNode");
            if (got.HeatLevel != 5) fails.Add("heatLevel");
            if (got.PrepFaction != Faction.Legion) fails.Add("prepFaction");
            if (!got.CheckpointUsed) fails.Add("checkpointUsed");
            if (got.Contract != Contract.HighStakes) fails.Add("contract");

            // APPEND-ONLY GUARD: Objective is persisted as a raw ordinal (CardDto.Objective). If a
            // future edit reorders/removes a member, saved runs load the wrong objective. Check the
            // value order at runtime (Enum.GetValues is sorted by underlying value) so such a reorder
            // fails this test loudly instead of silently corrupting saves.
            var objVals = (Objective[])Enum.GetValues(typeof(Objective));
            if (objVals.Length < 8 || objVals[0] != Objective.Eliminate || objVals[7] != Objective.Decapitate)
                fails.Add("objectiveOrdinals");

            // Same append-only guard for every other enum persisted by raw (int) ordinal in the
            // DTOs (Unit weapon/perks/mods/traits, Run boons, mission faction). A future reorder or
            // removal silently corrupts existing saves — these checks make that fail SAVETEST loudly.
            var weaponVals = (WeaponKind[])Enum.GetValues(typeof(WeaponKind));
            if (weaponVals.Length < 5 || weaponVals[0] != WeaponKind.Rifle || weaponVals[weaponVals.Length - 1] != WeaponKind.Smg)
                fails.Add("weaponKindOrdinals");
            var perkVals = (Perk[])Enum.GetValues(typeof(Perk));
            if (perkVals.Length < 20 || perkVals[0] != Perk.LockOn || perkVals[perkVals.Length - 1] != Perk.Siegebreaker)
                fails.Add("perkOrdinals");
            var modVals = (WeaponMod[])Enum.GetValues(typeof(WeaponMod));
            if (modVals.Length < 4 || modVals[0] != WeaponMod.Scope || modVals[modVals.Length - 1] != WeaponMod.Stabilizer)
                fails.Add("weaponModOrdinals");
            var traitVals = (Trait[])Enum.GetValues(typeof(Trait));
            if (traitVals.Length < 4 || traitVals[0] != Trait.Killer || traitVals[traitVals.Length - 1] != Trait.Vengeful)
                fails.Add("traitOrdinals");
            var boonVals = (Boon[])Enum.GetValues(typeof(Boon));
            if (boonVals.Length < 10 || boonVals[0] != Boon.Marksmen || boonVals[boonVals.Length - 1] != Boon.RapidDeploy)
                fails.Add("boonOrdinals");
            var factionVals = (Faction[])Enum.GetValues(typeof(Faction));
            if (factionVals.Length < 4 || factionVals[0] != Faction.None || factionVals[factionVals.Length - 1] != Faction.Wardens)
                fails.Add("factionOrdinals");
            var specVals = (Spec[])Enum.GetValues(typeof(Spec));
            if (specVals.Length < 11 || specVals[0] != Spec.None || specVals[^1] != Spec.CombatMedic)
                fails.Add("specOrdinals");
            var scarVals = (Scar[])Enum.GetValues(typeof(Scar));
            if (scarVals.Length < 4 || scarVals[0] != Scar.ShellShocked || scarVals[^1] != Scar.Vendetta)
                fails.Add("scarOrdinals");
            var contractVals = (Contract[])Enum.GetValues(typeof(Contract));
            if (contractVals.Length < 4 || contractVals[0] != Contract.None || contractVals[^1] != Contract.Spearhead)
                fails.Add("contractOrdinals");
            // W9: MetaUnlock is persisted by ordinal in meta.json's Unlocks list — same append-only
            // guard (first + last member) so a reorder/removal fails SAVETEST loudly.
            var unlockVals = (MetaUnlock[])Enum.GetValues(typeof(MetaUnlock));
            if (unlockVals.Length < 6 || unlockVals[0] != MetaUnlock.StartIntel || unlockVals[^1] != MetaUnlock.StandingReserve)
                fails.Add("metaUnlockOrdinals");

            // meta (unlocked-max heat) round-trips through its own meta.json
            string metaSaved = File.Exists(MetaPath) ? File.ReadAllText(MetaPath) : null;
            try
            {
                SaveMetaHeat(4);
                if (LoadMetaHeat() != 4) fails.Add("metaHeat");
                SaveMetaHeat(99);                       // clamped to the ladder ceiling on read/write
                if (LoadMetaHeat() != Heat.Max) fails.Add("metaHeatClamp");

                // W3 WAR ROOM meta round-trips (all append-only, whole-DTO r-m-w — must not clobber heat).
                // salvage add/spend
                int s0 = LoadSalvage();
                AddSalvage(50);
                if (LoadSalvage() != s0 + 50) fails.Add("metaSalvageAdd");
                if (!SpendSalvage(30) || LoadSalvage() != s0 + 20) fails.Add("metaSalvageSpend");
                if (SpendSalvage(9999)) fails.Add("metaSalvageOverspend");   // must refuse + spend nothing
                if (LoadSalvage() != s0 + 20) fails.Add("metaSalvageOverspendMutated");
                // achievements: unlock is idempotent
                if (!UnlockAchievement("TEST_ACH")) fails.Add("metaAchNew");
                if (UnlockAchievement("TEST_ACH")) fails.Add("metaAchDup");
                if (!LoadAchievements().Contains("TEST_ACH")) fails.Add("metaAchLoad");
                // unlocks: add/has
                AddUnlock(2);
                if (!HasUnlock(2)) fails.Add("metaUnlockHas");
                if (HasUnlock(1)) fails.Add("metaUnlockPhantom");
                // legends: prepend (newest first) + cap at 40
                AddLegends(new[] { new LegendDto { Name = "ALPHA", Cls = "ASSAULT", Rank = "SGT", Kills = 9, Heat = 3, Won = true } });
                AddLegends(new[] { new LegendDto { Name = "BRAVO", Cls = "RANGER", Rank = "PVT", Kills = 1, Heat = 0, Won = false } });
                var legs = LoadLegends();
                if (legs.Count < 2 || legs[0].Name != "BRAVO" || legs[1].Name != "ALPHA") fails.Add("metaLegendsPrepend");
                for (int i = 0; i < 60; i++) AddLegends(new[] { new LegendDto { Name = "F" + i } });
                if (LoadLegends().Count != 40) fails.Add("metaLegendsCap");
                // run totals
                var (r0, w0, b0) = LoadRunTotals();
                RecordRunTotals(true, 6);
                RecordRunTotals(false, 3);
                var (r1, w1, b1) = LoadRunTotals();
                if (r1 != r0 + 2 || w1 != w0 + 1 || b1 != Math.Max(b0, 6)) fails.Add("metaRunTotals");
                // whole-DTO r-m-w must NOT have clobbered heat set above (99 -> clamped Heat.Max)
                if (LoadMetaHeat() != Heat.Max) fails.Add("metaW3ClobberedHeat");
            }
            finally
            {
                if (metaSaved != null) { try { File.WriteAllText(MetaPath, metaSaved); } catch { } }
                else { try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch { } }
            }

            // corrupt-file armor: garbage meta.json must never be silently wiped
            string corrupt = CorruptionSelfTest();
            if (corrupt != null) fails.Add(corrupt);

            return fails.Count == 0
                ? "SAVETEST: PASS (run round-trips squad/perks/weapon-mods/card/heat; meta heat round-trips; corrupt meta stashed to .bak, rewrite clean)"
                : "SAVETEST: FAIL (" + string.Join(",", fails) + ")";
        }
        catch (Exception e) { return "SAVETEST: FAIL (exception " + e.Message + ")"; }
        finally
        {
            if (saved != null) { try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, saved); } catch { } }
            else Delete();
        }
    }

    /// Corrupt-meta recovery check (dispatched from inside SelfTest, so SAVETEST covers it).
    /// A garbage meta.json must (a) read as a fresh profile (heat 0), (b) be stashed to
    /// meta.json.bak instead of destroyed, and (c) the next read-modify-write must land a
    /// clean reparsable meta.json (the atomic path) while the .bak still holds the garbage.
    /// Returns null on success, else a short failure tag for the SAVETEST fails list.
    /// Snapshots BOTH meta.json and any pre-existing meta.json.bak (a user's real crash
    /// evidence) and restores/deletes everything test-created in the finally.
    static string CorruptionSelfTest()
    {
        const string garbage = "{ this is *not* json ]]] ";
        string bakPath = MetaPath + ".bak";
        string metaSaved = File.Exists(MetaPath) ? File.ReadAllText(MetaPath) : null;
        string bakSaved = File.Exists(bakPath) ? File.ReadAllText(bakPath) : null;
        bool stashSaved = _metaEvidenceStashed;
        try
        {
            Directory.CreateDirectory(Dir);
            _metaEvidenceStashed = false;   // exercise the stash path regardless of session history
            File.WriteAllText(MetaPath, garbage);
            if (LoadMetaHeat() != 0) return "corruptMetaNotFresh";              // garbage reads as a fresh profile
            SaveMetaHeat(2);                                                    // the read-modify-write that used to wipe silently
            if (LoadMetaHeat() != 2) return "corruptMetaRewriteUnreadable";     // rewrite reparses clean
            if (!File.Exists(bakPath) || File.ReadAllText(bakPath) != garbage)
                return "corruptMetaEvidenceLost";                               // .bak holds the exact corrupt bytes
            return null;
        }
        catch (Exception e) { return "corruptMetaException:" + e.GetType().Name; }
        finally
        {
            _metaEvidenceStashed = stashSaved;
            if (metaSaved != null) { try { File.WriteAllText(MetaPath, metaSaved); } catch { } }
            else { try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch { } }
            if (bakSaved != null) { try { File.WriteAllText(bakPath, bakSaved); } catch { } }
            else { try { if (File.Exists(bakPath)) File.Delete(bakPath); } catch { } }
            try { if (File.Exists(MetaPath + ".tmp")) File.Delete(MetaPath + ".tmp"); } catch { }
        }
    }
}
