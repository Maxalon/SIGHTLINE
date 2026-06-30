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

    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best effort */ }
    }

    public static void Save(Run run)
    {
        if (run == null) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(ToDto(run), Opts));
        }
        catch { /* a failed save must never crash the game */ }
    }

    /// Load the saved run, or null if there is none / it is unreadable (and then
    /// a corrupt file is removed so the intro stops offering a broken CONTINUE).
    public static Run Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var dto = JsonSerializer.Deserialize<RunDto>(File.ReadAllText(FilePath), Opts);
            return dto == null ? null : FromDto(dto);
        }
        catch { Delete(); return null; }
    }

    // ---- meta persistence (Heat/Ascension unlock) ----
    // The max-unlocked Heat is META: it survives run end (unlike save.json, which is deleted
    // when a run ends). Stored in its own tiny meta.json. Gated by Game.NoPersist at the call
    // sites exactly like the run save, so the harness never touches disk.
    // meta.json carries several independent fields (MaxHeat, LossStreak). Always read-modify-write
    // the whole DTO so saving one field never clobbers another. Missing fields default to 0, so an
    // old meta.json (heat-only) still loads — append-only and forward-compatible.
    static MetaDto LoadMetaDto()
    {
        try { if (File.Exists(MetaPath)) return JsonSerializer.Deserialize<MetaDto>(File.ReadAllText(MetaPath), Opts) ?? new MetaDto(); }
        catch { }
        return new MetaDto();
    }

    static void WriteMetaDto(MetaDto dto)
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(MetaPath, JsonSerializer.Serialize(dto, Opts)); }
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

    class MetaDto { public int MaxHeat; public int LossStreak; }

    // ---- mapping ----
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
        };
        foreach (var u in r.Squad)
            dto.Squad.Add(new UnitDto
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
            });
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
        // regenerate the branching campaign map from its seed and restore the position
        if (dto.MapSeed != 0)
        {
            r.MapSeed = dto.MapSeed;
            r.GenerateMap(dto.MapSeed);
            r.MapPos = (dto.MapPos >= 0 && dto.MapPos < r.Map.Count) ? dto.MapPos : 0;
            if (r.CurrentNode != null) r.CurrentNode.Visited = true;
        }
        foreach (var d in dto.Squad)
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
                Spec = (Spec)d.Spec,   // append-only: old saves default 0 == Spec.None
            };
            if (d.Perks != null) foreach (var p in d.Perks) u.Perks.Add((Perk)p);
            // installed weapon mods: add them, then re-bake the freshly-built weapon's stats
            // BEFORE seeding ammo so an EXTENDED MAG is reflected in the starting clip.
            if (d.WeaponMods != null) foreach (var m in d.WeaponMods) u.WeaponMods.Add((WeaponMod)m);
            u.RefreshWeaponMods();
            u.Ammo = u.Weapon.Clip;
            if (d.Traits != null) foreach (var t in d.Traits) u.Traits.Add((Trait)t);
            if (d.Bonds != null) u.Bonds = new List<string>(d.Bonds);
            r.Squad.Add(u);
        }
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
            a.Perks.Add(Perk.Deadeye); a.Perks.Add(Perk.Tank);
            a.InstallMod(WeaponMod.Scope); a.InstallMod(WeaponMod.ExtendedMag);   // persistent weapon upgrades
            a.Nickname = "REAPER";
            a.Traits.Add(Trait.Killer); a.Traits.Add(Trait.IronWill);
            a.Bonds.Add("NOX");
            a.Spec = Spec.Breacher;   // a chosen class specialization fork must round-trip
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
            if (!g0.HasPerk(Perk.Deadeye) || !g0.HasPerk(Perk.Tank) || g0.Perks.Count != 2) fails.Add("perks");
            // weapon mods round-trip AND re-bake onto the rebuilt weapon's effective stats
            if (!g0.HasMod(WeaponMod.Scope) || !g0.HasMod(WeaponMod.ExtendedMag) || g0.WeaponMods.Count != 2) fails.Add("weaponMods");
            if (g0.Weapon.AimBonus != WeaponModDef.ScopeAim) fails.Add("weaponModScopeApplied");      // Shotgun base aimBonus 0 + scope
            if (g0.Weapon.Clip != 2 + WeaponModDef.MagClip) fails.Add("weaponModMagApplied");         // Shotgun base clip 2 + extended mag
            if (g0.Nickname != "REAPER") fails.Add("nickname");
            if (!g0.HasTrait(Trait.Killer) || !g0.HasTrait(Trait.IronWill) || g0.Traits.Count != 2) fails.Add("traits");
            if (g0.Bonds.Count != 1 || g0.Bonds[0] != "NOX") fails.Add("bonds");
            if (g0.Spec != Spec.Breacher) fails.Add("spec");
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
            if (perkVals.Length < 20 || perkVals[0] != Perk.LockOn || perkVals[perkVals.Length - 1] != Perk.Gunslinger)
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

            // meta (unlocked-max heat) round-trips through its own meta.json
            string metaSaved = File.Exists(MetaPath) ? File.ReadAllText(MetaPath) : null;
            try
            {
                SaveMetaHeat(4);
                if (LoadMetaHeat() != 4) fails.Add("metaHeat");
                SaveMetaHeat(99);                       // clamped to the ladder ceiling on read/write
                if (LoadMetaHeat() != Heat.Max) fails.Add("metaHeatClamp");
            }
            finally
            {
                if (metaSaved != null) { try { File.WriteAllText(MetaPath, metaSaved); } catch { } }
                else { try { if (File.Exists(MetaPath)) File.Delete(MetaPath); } catch { } }
            }

            return fails.Count == 0
                ? "SAVETEST: PASS (run round-trips squad/perks/weapon-mods/card/heat; meta heat round-trips)"
                : "SAVETEST: FAIL (" + string.Join(",", fails) + ")";
        }
        catch (Exception e) { return "SAVETEST: FAIL (exception " + e.Message + ")"; }
        finally
        {
            if (saved != null) { try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, saved); } catch { } }
            else Delete();
        }
    }
}
