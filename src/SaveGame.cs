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

    // ---- mapping ----
    static RunDto ToDto(Run r)
    {
        var dto = new RunDto { Mission = r.Mission, Intel = r.Intel, Fallen = new List<string>(r.Fallen) };
        foreach (var u in r.Squad)
            dto.Squad.Add(new UnitDto
            {
                Name = u.Name, Cls = u.Cls,
                Hp = u.Hp, MaxHp = u.MaxHp, Aim = u.Aim, Mobility = u.Mobility,
                Weapon = (int)u.Weapon.Kind, Kills = u.Kills, Rank = u.Rank,
                BonusGrenades = u.BonusGrenades,
                Perks = u.Perks.ConvertAll(p => (int)p),
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
        var r = new Run { Mission = dto.Mission, Intel = dto.Intel, Squad = new List<Unit>() };
        if (dto.Fallen != null) r.Fallen = new List<string>(dto.Fallen);
        foreach (var d in dto.Squad)
        {
            var u = new Unit
            {
                Name = d.Name, Cls = d.Cls, Team = Team.Player,
                Hp = d.Hp, MaxHp = d.MaxHp, Aim = d.Aim, Mobility = d.Mobility,
                Weapon = Weapon.Make((WeaponKind)d.Weapon),
                Kills = d.Kills, Rank = d.Rank, Alive = true,
                BonusGrenades = d.BonusGrenades,
            };
            u.Ammo = u.Weapon.Clip;
            if (d.Perks != null) foreach (var p in d.Perks) u.Perks.Add((Perk)p);
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
        public CardDto Card;
    }

    class UnitDto
    {
        public string Name, Cls;
        public int Hp, MaxHp, Aim, Mobility, Weapon, Kills, Rank, BonusGrenades;
        public List<int> Perks = new();
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
            var src = new Run { Mission = 4, Intel = 23, Squad = new List<Unit>() };
            src.Fallen.Add("DOWNED-GUY");
            var a = new Unit
            {
                Name = "VEGA", Cls = "ASSAULT", Team = Team.Player,
                Hp = 4, MaxHp = 11, Aim = 78, Mobility = 8,
                Weapon = Weapon.Make(WeaponKind.Rifle), Kills = 7, Rank = 3, BonusGrenades = 2,
            };
            a.Perks.Add(Perk.Deadeye); a.Perks.Add(Perk.Tank);
            src.Squad.Add(a);
            src.Squad.Add(new Unit { Name = "NOX", Cls = "SHARPSHOOTER", Team = Team.Player, Hp = 6, MaxHp = 6, Aim = 76, Mobility = 6, Weapon = Weapon.Make(WeaponKind.Sniper), Kills = 2, Rank = 1 });
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
            if (g0.Weapon.Kind != WeaponKind.Rifle) fails.Add("weapon");
            if (g0.BonusGrenades != 2) fails.Add("bonusGrenades");
            if (!g0.HasPerk(Perk.Deadeye) || !g0.HasPerk(Perk.Tank) || g0.Perks.Count != 2) fails.Add("perks");
            if (got.CurrentCard == null || got.CurrentCard.Objective != Objective.Hack ||
                got.CurrentCard.EnemyDelta != 2 || got.CurrentCard.Reward != RewardKind.BonusPerk)
                fails.Add("card");

            return fails.Count == 0
                ? "SAVETEST: PASS (run round-trips squad/perks/weapon/card)"
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
