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
public static partial class SaveGame
{
    // SpecialFolder.ApplicationData resolves to $XDG_CONFIG_HOME (when set AND the directory
    // already exists) else $HOME/.config on Linux -- so the real save dir is
    // ~/.config/Sightline (Linux) / %AppData%\\Sightline (Windows) / ~/Library/Application Support/Sightline (mac).
    // NOT ~/.local/share -- that is LocalApplicationData, which this game does not use.
    // Edge case worth knowing: GetFolderPath uses SpecialFolderOption.None, which returns "" when
    // the resolved directory does not exist yet. Path.Combine("", "Sightline") would then be a
    // RELATIVE dir next to the process CWD, scattering saves per-launch-directory -- so fall back
    // to $HOME/.config/Sightline (which Save/WriteMetaDto create on demand) when that happens.
    static string Dir
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME") ?? ".";
                root = Path.Combine(home, ".config");
            }
            return Path.Combine(root, "Sightline");
        }
    }
    /// The one config directory this game writes to, with the empty-ApplicationData fallback
    /// above already applied. Public so `Display` can share the guard instead of re-deriving the
    /// path (R1 review: it re-derived it WITHOUT the fallback and could write a relative path).
    public static string ConfigDir => Dir;
    static string FilePath => Path.Combine(Dir, "save.json");
    static string MetaPath => Path.Combine(Dir, "meta.json");

    /// Save-format schema version stamped into every file this build writes (RunDto/MetaDto
    /// .SchemaVersion). Files written before the field existed read back as 0. Bump this in the
    /// same commit as any change that DEFAULTS CANNOT RESCUE -- a field whose type or meaning
    /// changed -- and branch on the stored value in FromDto / LoadMetaDto. Purely additive fields
    /// still need no bump: they default inert on their own.
    public const int CurrentSchema = 1;

    // Serialization goes through a SOURCE-GENERATED context, not reflection. Reflection-based
    // System.Text.Json needs type metadata that `dotnet publish -p:PublishTrimmed=true` strips:
    // the game booted, played and finished a whole campaign on a trimmed build while silently
    // losing every save and the entire cross-run meta profile (measured -- SAVETEST and METATEST
    // both failed against the trimmed binary). The generator emits the (de)serializers at compile
    // time, so the trimmer can see them and a trimmed build persists correctly. Keep every new DTO
    // reachable from one of the [JsonSerializable] roots below.
    [System.Text.Json.Serialization.JsonSourceGenerationOptions(IncludeFields = true, WriteIndented = true)]
    [System.Text.Json.Serialization.JsonSerializable(typeof(RunDto))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(MetaDto))]
    internal partial class SaveJson : System.Text.Json.Serialization.JsonSerializerContext { }

    // D2: the intro polls Exists EVERY FRAME (Hud draws CONTINUE off it), so it cannot re-parse
    // save.json each time -- memoise the verdict against the file's (write-time, length) so an
    // external edit still forces a re-validation. Cleared implicitly when the file goes away.
    static long _existsTicks = -1, _existsLen = -1;
    static bool _existsVerdict;

    /// Drop the memoised Exists verdict. Called after every write/delete of save.json so a rewrite
    /// that happens to land the same (write-time, length) as the previous file can never be judged
    /// by the stale answer.
    static void InvalidateExistsCache() { _existsTicks = _existsLen = -1; }

    /// True only when a save exists AND is structurally usable. A structurally-VALID-but-empty
    /// save ({}, null, or one whose Squad key was renamed) used to leave CONTINUE drawn forever
    /// over a run that could never load; validating here routes it through Load's stash-and-remove
    /// so the offer disappears on its own and the bytes survive as save.json.bak.
    public static bool Exists
    {
        get
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (!fi.Exists) { _existsTicks = _existsLen = -1; return _existsVerdict = false; }
                long ticks = fi.LastWriteTimeUtc.Ticks, len = fi.Length;
                if (ticks == _existsTicks && len == _existsLen) return _existsVerdict;
                _existsTicks = ticks; _existsLen = len;
                return _existsVerdict = (Load() != null);
            }
            catch { return false; }
        }
    }

    /// Test-only path exposure (MODETEST abandon leg preserves/restores any real save.json,
    /// mirroring MetaPathPublic). Not used by gameplay code.
    public static string SavePathPublic => FilePath;

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best effort */ }
        InvalidateExistsCache();
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
            File.WriteAllText(tmp, JsonSerializer.Serialize(ToDto(run), SaveJson.Default.RunDto));
            File.Move(tmp, FilePath, overwrite: true);
            InvalidateExistsCache();
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
            var dto = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SaveJson.Default.RunDto);
            // D2: "parses fine but is unusable" is corruption too. `null`, `{}`, or a save whose
            // Squad key was renamed/emptied deserializes WITHOUT throwing, so this used to return
            // null and leave the file in place -- Game.ContinueRun refused it (empty squad) while
            // Hud kept drawing CONTINUE off SaveGame.Exists, giving a button that did nothing,
            // forever, with no banner and no stash. Route it through the same recovery path as an
            // unparseable file: the squad is the one field a resumable run cannot do without.
            if (dto == null || dto.Squad == null || dto.Squad.Count == 0) { StashCorruptSave(); return null; }
            // R2 (LOW-2): SchemaVersion was WRITTEN and self-tested but never READ, so a file
            // stamped 999 loaded silently — the one thing the field exists to prevent. We cannot
            // know what a FUTURE build meant by its fields, so refuse it rather than misread it;
            // the stash keeps the file so a newer build can still load it. Older files (0 = written
            // before the field, or any version up to ours) still load: every change so far has been
            // additive, which is exactly what CurrentSchema's contract says a bump is NOT for.
            if (dto.SchemaVersion > CurrentSchema) { StashCorruptSave(); return null; }
            return FromDto(dto);
        }
        catch
        {
            StashCorruptSave();
            return null;
        }
    }

    /// Move an unusable save.json aside to save.json.bak: evidence preserved, and the intro stops
    /// offering a CONTINUE that cannot work (Exists goes false once the file is gone). Recovery I/O
    /// must never crash -- we are already inside a failure path.
    static void StashCorruptSave()
    {
        try { if (File.Exists(FilePath)) File.Move(FilePath, FilePath + ".bak", overwrite: true); }
        catch { }
        InvalidateExistsCache();
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
        try { if (File.Exists(MetaPath)) return JsonSerializer.Deserialize(File.ReadAllText(MetaPath), SaveJson.Default.MetaDto) ?? new MetaDto(); }
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
            dto.SchemaVersion = CurrentSchema;
            string tmp = MetaPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, SaveJson.Default.MetaDto));
            File.Move(tmp, MetaPath, overwrite: true);
        }
        catch { /* a failed meta save must never crash the game */ }
    }

    /// The highest heat rung the profile has UNLOCKED. R2 FIX 4: floored at 0, not at Heat.Min.
    /// W5 moved Heat.Clamp's floor to -1 (RECRUIT) — correct for a DIALLED level, wrong for an
    /// unlock CEILING, which starts at 0 and only ever rises. A corrupt/edited meta carrying
    /// {"MaxHeat":-9} clamped to -1, so Game.RefreshMeta set UnlockedHeat = -1, PendingHeat was
    /// pinned to -1, and BOTH intro steppers went dead (minus needs level > Heat.Min, plus needs
    /// level < unlocked) — the difficulty picker locked on RECRUIT with no way out but deleting
    /// meta.json. Game.cs:1727 already applies Math.Max(0, ...) on the SIGHTLINE_HEAT env path,
    /// so the invariant was known; this is the disk path that was missed. SaveMetaHeat floors
    /// the same way so a bad ceiling can never be written back either.
    public static int LoadMetaHeat() => Math.Clamp(LoadMetaDto().MaxHeat, 0, Heat.Max);

    public static void SaveMetaHeat(int maxHeat)
    {
        var d = LoadMetaDto(); d.MaxHeat = Math.Clamp(maxHeat, 0, Heat.Max); WriteMetaDto(d);   // R2 FIX 4: a CEILING floors at 0
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

    /// Record a daily WIN for `stamp` and bank its `bounty` in the SAME atomic meta write. Pays out
    /// at most once per stamp: returns (paid=false, nothing written) if this stamp already paid. The
    /// streak increments when `stamp` is the calendar day AFTER the last paid win; any gap (or a
    /// fresh profile) resets it to 1. The pay and the paid-mark land in ONE WriteMetaDto, so a crash
    /// can never mark the stamp paid without the salvage — nor pay without marking (a double-pay).
    public static (bool paid, int streak) RecordDailyWin(int stamp, int bounty)
    {
        var d = LoadMetaDto();
        if (d.DailyWinStamp == stamp) return (false, Math.Max(0, d.DailyStreak));   // already paid today
        d.DailyStreak = IsNextDay(d.DailyWinStamp, stamp) ? Math.Max(0, d.DailyStreak) + 1 : 1;
        d.DailyWinStamp = stamp;
        d.Salvage = Math.Max(0, d.Salvage) + Math.Max(0, bounty);   // pay + mark, one write
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
    internal class MetaDto
    {
        /// Migration hook -- see RunDto.SchemaVersion. 0 on every profile written before it existed.
        public int SchemaVersion;
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
    // R2 (LOW-1) — SCALAR SANITY BOUNDS for a loaded soldier. F1 hardened the persisted ENUM
    // ordinals (EnumOr / AddDefined) but left every scalar to load verbatim: {"Mobility":1000000}
    // gave a MoveBudget of 2,000,000 half-steps (a soldier that reaches any tile on the board,
    // and a Dijkstra flood that walks the whole grid every hover), {"Mobility":-9} a MoveBudget of
    // 2 (a soldier that cannot cross a tile), {"Aim":100000}, {"Armor":-50} (armor that ADDS
    // damage through HardenedReduce), {"Rank":99} (Unit.RankName only survives because it clamps).
    // These are generous envelopes around what the game can legitimately produce, not gameplay
    // caps — the shop's own limits (Unit.ArmorMax etc.) still govern real play. A save inside the
    // envelope is byte-identical after a round trip, so SAVETEST's fingerprints are untouched.
    const int MaxHpCap = 99, AimCap = 100, MobilityCap = 20, ArmorCap = 10, WoundCap = 20, GrenadeCap = 9;

    static Unit FromUnitDto(UnitDto d, bool fromReserve = false)
    {
        int maxHp = Math.Clamp(d.MaxHp, 1, MaxHpCap);
        var u = new Unit
        {
            Name = d.Name, Cls = d.Cls, Team = Team.Player,
            Hp = Math.Clamp(d.Hp, 1, maxHp), MaxHp = maxHp,
            Aim = Math.Clamp(d.Aim, 1, AimCap), Mobility = Math.Clamp(d.Mobility, 1, MobilityCap),
            Weapon = Weapon.Make(EnumOr(d.Weapon, WeaponKind.Rifle)),
            Kills = Math.Max(0, d.Kills), Rank = Math.Clamp(d.Rank, 0, Run.Ranks.Length - 1), Alive = true,
            BonusGrenades = Math.Clamp(d.BonusGrenades, 0, GrenadeCap), CustomTag = d.CustomTag,
            Wound = Math.Clamp(d.Wound, 0, WoundCap),
            Armor = Math.Clamp(d.Armor, 0, ArmorCap),
            Nickname = d.Nickname, Benched = d.Benched,
            Spec = EnumOr(d.Spec, Spec.None),
            FromReserve = fromReserve,
        };
        AddDefined(u.Perks, d.Perks);
        AddDefined(u.WeaponMods, d.WeaponMods);
        u.RefreshWeaponMods();
        u.Ammo = u.Weapon.Clip;
        AddDefined(u.Traits, d.Traits);
        if (d.Bonds != null) u.Bonds = new List<string>(d.Bonds);
        AddDefined(u.Scars, d.Scars);
        u.VendettaFaction = EnumOr(d.VendettaFaction, Faction.None);
        u.NearDeathCount = Math.Max(0, d.NearDeathCount);
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

    /// FUL-10 (LIVING LEGENDS): permanently ERASE reserve records by name — the same key
    /// EnshrineVeterans dedupes on, so "the fallen ∩ the reserve" is exactly what dies.
    /// Returns how many records were erased; unmatched names are a no-op (no write).
    public static int RemoveVeterans(IEnumerable<string> names)
    {
        if (names == null) return 0;
        var doomed = new HashSet<string>(names);
        if (doomed.Count == 0) return 0;
        var d = LoadMetaDto();
        if (d.Veterans == null || d.Veterans.Count == 0) return 0;
        int n = d.Veterans.RemoveAll(v => doomed.Contains(v.Name));
        if (n > 0) WriteMetaDto(d);
        return n;
    }

    static RunDto ToDto(Run r)
    {
        var dto = new RunDto
        {
            SchemaVersion = CurrentSchema,
            Mission = r.Mission, Intel = r.Intel, Fallen = new List<string>(r.Fallen),
            BondTally = new Dictionary<string, int>(r.BondTally),
            MapSeed = r.MapSeed, MapPos = r.MapPos,
            HeatLevel = r.HeatLevel,
            ActiveBoons = r.ActiveBoons.ConvertAll(b => (int)b),
            PrepFaction = (int)r.PrepFaction,
            CheckpointUsed = r.CheckpointUsed,
            Contract = (int)r.Contract,
            PendingSalvageReward = r.PendingSalvageReward,
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

    // ---- D5: defensive enum reads --------------------------------------------------------
    // Persisted ordinals are cast straight out of the DTOs. A hand-edited save, or one written by
    // a NEWER build that appended members, carries values this build has no member for -- the cast
    // is legal C# so nothing throws and the bogus value reaches gameplay. (Measured: `Objective:99`
    // loaded and ran, and Game.CheckEnd's final `else` treated the unknown objective as Evac, so
    // the mission had an unreachable win condition until the squad wiped.) Unknown -> a safe
    // fallback; unknown members of a LIST are dropped rather than defaulted, since a bogus perk
    // silently becoming Perk[0] would be a stealth buff.
    static T EnumOr<T>(int raw, T fallback) where T : struct, Enum
        => Enum.IsDefined(typeof(T), raw) ? (T)(object)raw : fallback;

    static void AddDefined<T>(List<T> into, List<int> raw) where T : struct, Enum
    {
        if (raw == null) return;
        foreach (int v in raw) if (Enum.IsDefined(typeof(T), v)) into.Add((T)(object)v);
    }

    static Run FromDto(RunDto dto)
    {
        var r = new Run { Mission = dto.Mission, Intel = dto.Intel, Squad = new List<Unit>(), HeatLevel = Heat.Clamp(dto.HeatLevel) };
        if (dto.Fallen != null) r.Fallen = new List<string>(dto.Fallen);
        if (dto.BondTally != null) r.BondTally = new Dictionary<string, int>(dto.BondTally);
        AddDefined(r.ActiveBoons, dto.ActiveBoons);
        r.PrepFaction = EnumOr(dto.PrepFaction, Faction.None);   // append-only: old saves default 0 == Faction.None
        r.CheckpointUsed = dto.CheckpointUsed;      // append-only: old saves default false
        r.Contract = EnumOr(dto.Contract, Contract.None);        // append-only: old saves default 0 == Contract.None
        r.PendingSalvageReward = dto.PendingSalvageReward;   // append-only: FUL-10 event salvage claim (old saves default 0)
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
                // an unknown objective falls back to Eliminate: the one goal that is always
                // reachable, so a mangled card degrades to a winnable fight, not a soft-lock.
                Objective = EnumOr(cd.Objective, Objective.Eliminate), ModName = cd.ModName,
                EnemyDelta = cd.EnemyDelta, StatDelta = cd.StatDelta,
                Reward = EnumOr(cd.Reward, RewardKind.None), RewardText = cd.RewardText,
            };
        return r;
    }

    // ---- DTOs (public fields, IncludeFields = true) ----
    internal class RunDto
    {
        /// Migration hook. Additive fields default safely on their own, so this is 0 on every save
        /// written before it existed and stays 0 until a change actually needs a migration (a field
        /// whose TYPE or MEANING changed, which defaults cannot rescue). Bump it in the same commit
        /// as such a change and branch on it in FromDto. Persisted; do not repurpose.
        public int SchemaVersion;
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
        public int PendingSalvageReward;   // append-only: FUL-10 event salvage awaiting the run-end commit (old saves default 0)
    }

    internal class UnitDto
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

    internal class CardDto
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
            src.PendingSalvageReward = 25;      // FUL-10: an event's pending salvage claim must round-trip
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
            a.InstallMod(WeaponMod.Suppressor);   // W10: a NEW-TAIL mod must round-trip by ordinal (stat-silent)
            a.Nickname = "REAPER";
            a.Traits.Add(Trait.Killer); a.Traits.Add(Trait.IronWill);
            a.Bonds.Add("NOX");
            a.Spec = Spec.Breacher;   // a chosen class specialization fork must round-trip
            // W5 SCARS: earned trauma identity must round-trip (scars by ordinal + vendetta + count)
            a.Scars.Add(Scar.ShellShocked); a.Scars.Add(Scar.Vendetta);
            a.VendettaFaction = Faction.Wardens;
            a.NearDeathCount = 2;
            // FUL-7 belt-and-suspenders: gameplay guarantees Downed can never exist at the
            // mission-START checkpoint (EnterBarracks resolves every Downed first) — but even a
            // hand-built downed-and-recovered soldier must persist ONLY Hp/Wound/scars: the DOWN
            // transients are save-inert by the ToUnitDto whitelist, by construction.
            a.Downed = true; a.Stabilized = true; a.DownedTurns = 2;
            a.WasDownedThisMission = true; a.DownedByCls = "GRUNT";
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
            // the migration hook must actually be stamped on disk, not just declared
            int diskSchema = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SaveJson.Default.RunDto).SchemaVersion;
            var got = Load();
            if (got == null) return "SAVETEST: FAIL (load returned null)";

            var fails = new List<string>();
            if (diskSchema != CurrentSchema) fails.Add("runSchemaVersion");
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
            if (!g0.HasMod(WeaponMod.Scope) || !g0.HasMod(WeaponMod.ExtendedMag)
                || !g0.HasMod(WeaponMod.Suppressor) || g0.WeaponMods.Count != 3) fails.Add("weaponMods");
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
            // FUL-7: the DOWN state machine is transient — none of it round-trips (whitelist)
            if (g0.Downed || g0.Stabilized || g0.DownedTurns != 0
                || g0.WasDownedThisMission || g0.DownedByCls != null) fails.Add("downStatePersisted");
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
            if (got.PendingSalvageReward != 25) fails.Add("pendingSalvageReward");   // FUL-10

            // APPEND-ONLY GUARD (golden fingerprints). Every enum in PersistedEnums below is
            // stored BY ORDINAL -- a raw int in a DTO, or in meta.json's Unlocks list. Reordering,
            // removing, renaming or INSERTING a member silently re-points every save an older build
            // wrote. This used to be pinned positionally (first + last member, occasionally one in
            // the middle), which a mid-enum insertion walked straight past: inserting a perk at
            // index 5 of Perk shifted 18 ordinals, corrupted every save, and still PASSED. Hashing
            // the whole ordered member list catches any shape change at all.
            EnumShapeFails(fails);

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
                if (LoadMetaDto().SchemaVersion != CurrentSchema) fails.Add("metaSchemaVersion");
            }
            finally
            {
                if (metaSaved != null) { try { File.WriteAllText(MetaPath, metaSaved); } catch { } }
                else { try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch { } }
            }

            // corrupt-file armor: garbage meta.json must never be silently wiped
            string corrupt = CorruptionSelfTest();
            if (corrupt != null) fails.Add(corrupt);

            // D2/D5: structurally-valid-but-unusable saves, and out-of-range enum ordinals on read
            string structure = StructureSelfTest();
            if (structure != null) fails.Add(structure);

            return fails.Count == 0
                ? "SAVETEST: PASS (run round-trips squad/perks/weapon-mods/card/heat; schema stamped; 13 persisted-enum fingerprints match; meta heat round-trips; corrupt meta stashed to .bak, rewrite clean; unusable saves stashed + un-offered; junk ordinals clamped)"
                : "SAVETEST: FAIL (" + string.Join(",", fails) + ")"
                  + (fails.Exists(f => f.StartsWith("enumShape:")) ? EnumShapeAdvice : "");
        }
        catch (Exception e) { return "SAVETEST: FAIL (exception " + e.Message + ")"; }
        finally
        {
            if (saved != null) { try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, saved); } catch { } }
            else Delete();
        }
    }

    // ---- APPEND-ONLY ENUM GUARD (golden fingerprints) -------------------------------------
    // FNV-1a over "<value>:<NAME>;" for every member in underlying-value order. Enum.GetValues is
    // sorted by value, so the digest covers the exact ordinal->name mapping that saves depend on.
    static uint EnumFingerprint(Type t)
    {
        uint h = 2166136261u;
        var vals = Enum.GetValues(t);
        foreach (var v in vals)
        {
            string s = Convert.ToInt64(v).ToString(System.Globalization.CultureInfo.InvariantCulture)
                       + ":" + Enum.GetName(t, v) + ";";
            foreach (char c in s) { h ^= c; h *= 16777619u; }
        }
        return h;
    }

    /// Every enum persisted by raw ordinal, with the fingerprint of its committed shape.
    /// TO ADD A MEMBER: append it at the END of the enum, run SIGHTLINE_SAVETEST=1, and paste the
    /// "actual" hash it prints in here. Anything other than an append is a save-format break.
    static readonly (Type Type, uint Golden)[] PersistedEnums =
    {
        (typeof(Objective),     0x65158518u),
        (typeof(WeaponKind),    0x00BF6448u),
        (typeof(Perk),          0xEADD48BAu),
        (typeof(WeaponMod),     0xB2635D54u),
        (typeof(Trait),         0xB4F9F2EAu),
        (typeof(Boon),          0xD35220A4u),
        (typeof(SecondaryKind), 0x605C1DA5u),
        (typeof(Faction),       0x4C8FFBCFu),
        (typeof(Spec),          0xD1E12AEDu),
        (typeof(Scar),          0xB165F9D9u),
        (typeof(Contract),      0x9EC11430u),
        (typeof(MetaUnlock),    0xC672FAD5u),
        (typeof(RewardKind),    0x388AFEA8u),   // persisted as CardDto.Reward (raw int)
    };

    /// Guidance appended to a FAILing SAVETEST report when an enum's shape moved. Kept next to the
    /// table so the dev who trips it is told, in the failure itself, what is and is not safe.
    const string EnumShapeAdvice =
        "\n  >> A persisted enum changed shape. Ordinals ARE the save format: appending a member at "
      + "the END is safe (old saves keep their meaning); inserting, reordering, removing or renaming "
      + "one silently re-points every existing save and every meta.json profile. If you appended, "
      + "paste the actual hash above into SaveGame.PersistedEnums. If you did anything else, undo it.";

    static void EnumShapeFails(List<string> fails)
    {
        foreach (var (t, golden) in PersistedEnums)
        {
            uint got = EnumFingerprint(t);
            if (got != golden)
                fails.Add("enumShape:" + t.Name + " (golden 0x" + golden.ToString("X8")
                          + ", actual 0x" + got.ToString("X8") + ")");
        }
    }

    /// D2 + D5 armor (dispatched from inside SelfTest, so SAVETEST covers both).
    /// D2: a save that PARSES but cannot produce a resumable run (`null`, `{}`, a renamed/emptied
    /// Squad key) must be treated exactly like an unparseable one -- stashed to save.json.bak and
    /// removed -- so SaveGame.Exists goes false and the intro stops drawing a CONTINUE button that
    /// silently does nothing. D5: out-of-range enum ordinals from a hand-edited or newer-build save
    /// must not reach gameplay (an unknown Objective used to run the mission as Evac, i.e. with an
    /// unreachable win condition). Snapshots and restores save.json + save.json.bak.
    /// Returns null on success, else a short failure tag.
    static string StructureSelfTest()
    {
        string bakPath = FilePath + ".bak";
        string saved = File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        string bakSaved = File.Exists(bakPath) ? File.ReadAllText(bakPath) : null;
        try
        {
            Directory.CreateDirectory(Dir);

            // --- D2: each of these parses cleanly and yields no usable run.
            var dead = new (string Tag, string Json)[]
            {
                ("null",     "null"),
                ("empty",    "{}"),
                ("noSquad",  "{ \"Mission\": 3, \"Intel\": 5, \"Roster\": [] }"),
                ("emptySquad", "{ \"Mission\": 3, \"Squad\": [] }"),
            };
            foreach (var (tag, json) in dead)
            {
                try { if (File.Exists(bakPath)) File.Delete(bakPath); } catch { }
                File.WriteAllText(FilePath, json);
                InvalidateExistsCache();
                if (Exists) return "deadSaveStillOffered:" + tag;          // CONTINUE must not be drawn
                if (File.Exists(FilePath)) return "deadSaveNotRemoved:" + tag;
                if (!File.Exists(bakPath) || File.ReadAllText(bakPath) != json)
                    return "deadSaveEvidenceLost:" + tag;                  // bytes preserved verbatim
            }

            // --- D5: a well-formed save carrying impossible ordinals loads, clamps, and drops junk.
            var probe = new Run { Mission = 2, Intel = 1 };
            probe.Squad.Add(new Unit
            {
                Name = "PROBE", Cls = "ASSAULT", Team = Team.Player, Hp = 5, MaxHp = 5,
                Aim = 65, Mobility = 6, Weapon = Weapon.Make(WeaponKind.Rifle),
            });
            probe.CurrentCard = new MissionCard { Objective = Objective.Hack, Reward = RewardKind.None };
            Save(probe);
            string text = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize(text, SaveJson.Default.RunDto);
            doc.Card.Objective = 99;                 // no such objective in ANY build
            doc.Card.Reward = -5;
            doc.Squad[0].Weapon = 999;
            doc.Squad[0].Spec = -1;
            doc.Squad[0].Perks = new List<int> { 0, 999, -5 };
            doc.Squad[0].Traits = new List<int> { 12345 };
            doc.Squad[0].Scars = new List<int> { -2 };
            doc.PrepFaction = 77; doc.Contract = 77;
            doc.ActiveBoons = new List<int> { 0, 4242 };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(doc, SaveJson.Default.RunDto));
            InvalidateExistsCache();
            var back = Load();
            if (back == null) return "junkOrdinalsRejectedWholeSave";      // must degrade, not discard
            if (back.CurrentCard.Objective != Objective.Eliminate) return "junkObjectiveNotClamped";
            if (back.CurrentCard.Reward != RewardKind.None) return "junkRewardNotClamped";
            var pu = back.Squad[0];
            if (pu.Weapon.Kind != WeaponKind.Rifle) return "junkWeaponNotClamped";
            if (pu.Spec != Spec.None) return "junkSpecNotClamped";
            if (pu.Perks.Count != 1 || pu.Perks[0] != Perk.LockOn) return "junkPerksNotFiltered";
            if (pu.Traits.Count != 0) return "junkTraitsNotFiltered";
            if (pu.Scars.Count != 0) return "junkScarsNotFiltered";
            if (back.PrepFaction != Faction.None || back.Contract != Contract.None)
                return "junkRunEnumsNotClamped";
            if (back.ActiveBoons.Count != 1 || back.ActiveBoons[0] != Boon.Marksmen)
                return "junkBoonsNotFiltered";
            return null;
        }
        catch (Exception e) { return "structureException:" + e.GetType().Name; }
        finally
        {
            if (saved != null) { try { File.WriteAllText(FilePath, saved); } catch { } }
            else { try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { } }
            if (bakSaved != null) { try { File.WriteAllText(bakPath, bakSaved); } catch { } }
            else { try { if (File.Exists(bakPath)) File.Delete(bakPath); } catch { } }
            InvalidateExistsCache();
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
