using System;
using System.Collections.Generic;

namespace Sightline;

// PROGRAM HORIZON — Wave 6: CODEX / FIELD MANUAL (onboarding + legibility).
//
// A browsable in-game reference that surfaces the game's hidden depth (19 enemy archetypes, 5 classes,
// the perk / boon / contract / spec / trait / scar / weapon-mod / status vocabularies, and the objective
// rotation) so a new player can actually LEARN the systems. This is PRESENTATION/DATA ONLY: every entry
// is assembled from the EXISTING Name/Desc/Code static methods + enum iteration + a small hardcoded
// bestiary/class table whose blurbs are derived from the Ai.Plan behaviour comments in Mission.cs. It
// introduces NO balance content — nothing here is read by combat/AI/mission code.

/// One codex entry: a title, an optional short code chip, a wrapped-in-the-Hud description, and an
/// optional silhouette glyph key (a class/archetype string) drawn left of the text for ENEMIES/CLASSES.
public struct CodexEntry
{
    public string Title;
    public string Code;    // short chip (may be null/empty)
    public string Desc;
    public string Glyph;   // archetype/class string for Renderer.DrawCodexGlyph, or null for text-only
}

/// One category tab: a heading + its ordered entries.
public class CodexCategory
{
    public string Name;
    public List<CodexEntry> Entries = new();
    public bool HasGlyph;   // ENEMIES/CLASSES draw a silhouette preview per entry
}

/// Assembles the CODEX categories from existing game data. Pure data; safe to call any time.
public static class Codex
{
    /// The full ordered category list, rebuilt each call (cheap — cached by Game.BeginCodex on entry).
    public static List<CodexCategory> Build()
    {
        return new List<CodexCategory>
        {
            Enemies(),
            Classes(),
            Category("PERKS",       PerkEntries()),
            Category("BOONS",       BoonEntries()),
            Category("CONTRACTS",   ContractEntries()),
            Category("SPECS",       SpecEntries()),
            Category("TRAITS",      TraitEntries()),
            Category("SCARS",       ScarEntries()),
            Category("WEAPON MODS", WeaponModEntries()),
            Category("STATUS",      StatusEntries()),
            Category("OBJECTIVES",  ObjectiveEntries()),
        };
    }

    static CodexCategory Category(string name, List<CodexEntry> entries, bool glyph = false)
        => new CodexCategory { Name = name, Entries = entries, HasGlyph = glyph };

    // ---------------- ENEMIES (bestiary) ----------------
    // One row per archetype. name = the enemy's in-mission callsign (from Mission.SelectArchetype);
    // Code = the archetype label; blurbs derive from the Ai.Plan behaviour comments in Mission.cs.
    // Glyph = the archetype string so Renderer.DrawCodexGlyph draws the exact board silhouette.
    static readonly (string Name, string Cls, string Blurb)[] Bestiary =
    {
        ("RAIDER",  "GRUNT",     "Rank-and-file rifleman. Advances behind cover; the baseline threat."),
        ("STALKER", "SCOUT",     "Fast SMG screen. Probes the line and closes to short range."),
        ("VIPER",   "SNIPER",    "Kites to range and seeks height; deadly at distance, weak up close."),
        ("SENTRY",  "TURRET",    "Immobile nest — never moves, holds overwatch for free. Flank or bypass it."),
        ("REAVER",  "BERSERKER", "Tanky shotgun rusher; charges the nearest soldier. Kill it before it lands."),
        ("WASP",    "DRONE",     "Hovers and ignores the target's cover; beelines in a straight line."),
        ("AEGIS",   "SHIELD",    "Frontal barrier gives full cover from the front — flank it or hit from above."),
        ("BREACH",  "SAPPER",    "Demolition trooper — closes on your cover and blows it apart. Deny its approach."),
        ("ORDERLY", "MEDIC",     "Support hostile that heals wounded allies. Focus it to stop the sustain."),
        ("OGRE",    "BRUISER",   "Heavy LMG bruiser — high HP, suppresses at medium range."),
        ("JACKAL",  "HUNTER",    "Flanker — seeks tiles that expose your soldiers' unprotected side."),
        ("HOPLITE", "LANCER",    "Formation trooper — bunches into a wall and presses forward. A grenade lure."),
        ("FERAL",   "HOUND",     "Fast, low-HP swarmer; hunts your most-isolated soldier. Stay massed."),
        ("MORTAR",  "MORTAR",    "Back-line grenadier with a deep frag pouch. Lobs at clusters from cover."),
        ("BEACON",  "SPOTTER",   "Fragile designator — paints your priority target, amplifying enemy focus-fire. Kill it first."),
        ("SIEGE",   "BOMBARD",   "Artillery — telegraphs a cover-ignoring 3x3 strike a turn ahead. Relocate or kill it."),
        ("WRAITH",  "STRIKER",   "Fast flanker that slips past overwatch and strikes your soldiers' exposed side. Body-block it or focus it — it's glass."),
        ("HAZE",    "SCREENER",  "Back-line zoner — drops smoke on your firing lane to blind your shots. Reposition through the cloud, or kill it before it screens."),
        ("WARLORD", "ELITE",     "Named boss / mid-boss — tough, carries grenades, and rages at low HP."),
    };

    static CodexCategory Enemies()
    {
        var e = new List<CodexEntry>();
        foreach (var b in Bestiary)
            e.Add(new CodexEntry { Title = b.Name, Code = b.Cls, Desc = b.Blurb, Glyph = b.Cls });
        return Category("ENEMIES", e, glyph: true);
    }

    // ---------------- CLASSES (5) ----------------
    // Role line + the class's signature verb (name + Unit.AbilityDesc) pulled from AbilityKindFor.
    static readonly (string Cls, string Role)[] Classes_ =
    {
        ("ASSAULT",      "Front-line rifleman — pushes up and cracks open cover."),
        ("RANGER",       "Fast flanker — owns close range with a punchy shotgun."),
        ("SHARPSHOOTER", "Marksman — devastating at long range, weak point-blank."),
        ("GUNNER",       "Heavy weapons — top HP, suppresses and denies zones."),
        ("CORPSMAN",     "Field medic — the squad's only in-combat sustain."),
    };

    static CodexCategory Classes()
    {
        var e = new List<CodexEntry>();
        foreach (var c in Classes_)
        {
            var kind = Unit.AbilityKindFor(c.Cls);
            // reuse a stub Unit's AbilityName/AbilityDesc (derived from Cls) so the codex can't drift.
            var stub = new Unit { Cls = c.Cls };
            string verb = stub.AbilityName;
            string vdesc = stub.AbilityDesc;
            string desc = c.Role + "\nSIGNATURE: " + verb + " — " + vdesc;
            e.Add(new CodexEntry { Title = c.Cls, Code = verb, Desc = desc, Glyph = c.Cls });
        }
        return Category("CLASSES", e, glyph: true);
    }

    // ---------------- data-driven categories (iterate the enum) ----------------
    static List<CodexEntry> PerkEntries()
    {
        var e = new List<CodexEntry>();
        // iterate the FULL enum (incl. the retired-from-offers perks) so any already-earned perk is
        // documented; CODEXTEST asserts each has Name + Desc.
        foreach (Perk p in Enum.GetValues(typeof(Perk)))
            e.Add(new CodexEntry { Title = PerkDef.Name(p), Code = PerkDef.Code(p), Desc = PerkDef.Desc(p) });
        return e;
    }

    static List<CodexEntry> BoonEntries()
    {
        var e = new List<CodexEntry>();
        foreach (Boon b in Enum.GetValues(typeof(Boon)))
            e.Add(new CodexEntry { Title = BoonDef.Name(b), Code = BoonDef.Code(b), Desc = BoonDef.Desc(b) });
        return e;
    }

    static List<CodexEntry> ContractEntries()
    {
        var e = new List<CodexEntry>();
        foreach (Contract c in Enum.GetValues(typeof(Contract)))
            e.Add(new CodexEntry { Title = ContractDef.Name(c), Code = ContractDef.Code(c), Desc = ContractDef.Desc(c) });
        return e;
    }

    static List<CodexEntry> SpecEntries()
    {
        var e = new List<CodexEntry>();
        // skip Spec.None (the inert default — not a fork the player ever "has").
        foreach (Spec s in Enum.GetValues(typeof(Spec)))
        {
            if (s == Spec.None) continue;
            e.Add(new CodexEntry { Title = SpecDef.Name(s), Code = SpecDef.Code(s), Desc = SpecDef.Desc(s) });
        }
        return e;
    }

    static List<CodexEntry> TraitEntries()
    {
        var e = new List<CodexEntry>();
        foreach (Trait t in Enum.GetValues(typeof(Trait)))
            e.Add(new CodexEntry { Title = TraitDef.Name(t), Code = TraitDef.Code(t),
                                   Desc = TraitDef.Desc(t) + "  (earned by " + TraitDef.Feat(t) + ")" });
        return e;
    }

    static List<CodexEntry> ScarEntries()
    {
        var e = new List<CodexEntry>();
        foreach (Scar s in Enum.GetValues(typeof(Scar)))
            e.Add(new CodexEntry { Title = ScarDef.Name(s), Code = ScarDef.Code(s),
                                   Desc = ScarDef.Desc(s) + "  (left by " + ScarDef.Trauma(s) + ")" });
        return e;
    }

    static List<CodexEntry> WeaponModEntries()
    {
        var e = new List<CodexEntry>();
        foreach (WeaponMod m in Enum.GetValues(typeof(WeaponMod)))
            e.Add(new CodexEntry { Title = WeaponModDef.Name(m), Code = WeaponModDef.Code(m), Desc = WeaponModDef.Desc(m) });
        return e;
    }

    // StatusDef has Code + Name but no Desc — provide short descriptions here (derived from the
    // StatusKind enum doc + Unit magnitude consts) so the STATUS category reads clearly.
    public static string StatusDesc(StatusKind k) => k switch
    {
        StatusKind.Burning     => $"Damage over time at the start of each turn ({Unit.BurnDamage}/turn).",
        StatusKind.Bleed       => $"Damage over time for each tile moved ({Unit.BleedDamage}/tile).",
        StatusKind.Stun        => "Lose one action on your next turn.",
        StatusKind.Disoriented => $"-{Unit.DisorientAim} aim and cannot hold overwatch.",
        _ => "",
    };

    static List<CodexEntry> StatusEntries()
    {
        var e = new List<CodexEntry>();
        foreach (StatusKind k in Enum.GetValues(typeof(StatusKind)))
            e.Add(new CodexEntry { Title = StatusDef.Name(k), Code = StatusDef.Code(k), Desc = StatusDesc(k) });
        return e;
    }

    // Objective enum has no Def class — provide the readable name + a one-line goal here (mirrors the
    // HUD objective readout / Hud.DrawObjectiveIcon semantics). Data only.
    public static string ObjectiveName(Objective o) => o switch
    {
        Objective.Eliminate  => "ELIMINATE",
        Objective.Evac       => "EVAC",
        Objective.Hack       => "HACK",
        Objective.Escort     => "ESCORT",
        Objective.Sabotage   => "SABOTAGE",
        Objective.Rescue     => "RESCUE",
        Objective.Defend     => "DEFEND",
        Objective.Decapitate => "DECAPITATE",
        _ => o.ToString().ToUpperInvariant(),
    };

    public static string ObjectiveDesc(Objective o) => o switch
    {
        Objective.Eliminate  => "Destroy every hostile on the field.",
        Objective.Evac       => "Get all living soldiers into the extraction zone.",
        Objective.Hack       => "Reach the terminal and hold it to hack it down (goes loud).",
        Objective.Escort     => "Walk a fragile VIP to extraction alive — losing it fails the mission.",
        Objective.Sabotage   => "Plant demolition charges on all three sites (goes loud).",
        Objective.Rescue     => "Free the caged captive, then escort it to extraction.",
        Objective.Defend     => "Hold out for a set number of turns as reinforcement waves spawn.",
        Objective.Decapitate => "Kill the marked high-value target; peel its bodyguards first.",
        _ => "",
    };

    static List<CodexEntry> ObjectiveEntries()
    {
        var e = new List<CodexEntry>();
        foreach (Objective o in Enum.GetValues(typeof(Objective)))
            e.Add(new CodexEntry { Title = ObjectiveName(o), Desc = ObjectiveDesc(o) });
        return e;
    }

    // ---------------- CODEXTEST (content-completeness guard) ----------------
    /// Assert every enum member of the documented vocabularies has a non-empty Name AND Desc, and that
    /// the bestiary covers every archetype string used by Mission.SelectArchetype. A genuinely useful
    /// guard: a future enum addition that forgets its strings fails here loudly.
    public static string SelfTest()
    {
        var fails = new List<string>();

        void Chk(string cat, string key, string name, string desc)
        {
            if (string.IsNullOrWhiteSpace(name)) fails.Add($"{cat}:{key} empty NAME");
            if (string.IsNullOrWhiteSpace(desc)) fails.Add($"{cat}:{key} empty DESC");
        }

        foreach (Perk p in Enum.GetValues(typeof(Perk)))       Chk("PERK", p.ToString(), PerkDef.Name(p), PerkDef.Desc(p));
        foreach (Boon b in Enum.GetValues(typeof(Boon)))       Chk("BOON", b.ToString(), BoonDef.Name(b), BoonDef.Desc(b));
        foreach (Contract c in Enum.GetValues(typeof(Contract))) Chk("CONTRACT", c.ToString(), ContractDef.Name(c), ContractDef.Desc(c));
        foreach (Spec s in Enum.GetValues(typeof(Spec)))
        {
            if (s == Spec.None) continue;   // inert default: not documented / not a real fork
            Chk("SPEC", s.ToString(), SpecDef.Name(s), SpecDef.Desc(s));
        }
        foreach (Scar s in Enum.GetValues(typeof(Scar)))       Chk("SCAR", s.ToString(), ScarDef.Name(s), ScarDef.Desc(s));
        foreach (Trait t in Enum.GetValues(typeof(Trait)))     Chk("TRAIT", t.ToString(), TraitDef.Name(t), TraitDef.Desc(t));
        foreach (WeaponMod m in Enum.GetValues(typeof(WeaponMod))) Chk("WEAPONMOD", m.ToString(), WeaponModDef.Name(m), WeaponModDef.Desc(m));
        foreach (StatusKind k in Enum.GetValues(typeof(StatusKind))) Chk("STATUS", k.ToString(), StatusDef.Name(k), StatusDesc(k));
        foreach (Objective o in Enum.GetValues(typeof(Objective))) Chk("OBJECTIVE", o.ToString(), ObjectiveName(o), ObjectiveDesc(o));

        // classes: role + verb desc present
        foreach (var c in Classes_)
        {
            var stub = new Unit { Cls = c.Cls };
            Chk("CLASS", c.Cls, c.Cls, c.Role);
            if (string.IsNullOrWhiteSpace(stub.AbilityDesc)) fails.Add($"CLASS:{c.Cls} empty ABILITY DESC");
        }

        // bestiary: cover every archetype string SelectArchetype can spawn (plus the ELITE boss slot).
        string[] required =
        {
            "GRUNT","SCOUT","SNIPER","TURRET","BERSERKER","DRONE","SHIELD","SAPPER","MEDIC",
            "BRUISER","HUNTER","LANCER","HOUND","MORTAR","SPOTTER","BOMBARD","STRIKER","SCREENER","ELITE",
        };
        var covered = new HashSet<string>();
        foreach (var b in Bestiary) covered.Add(b.Cls);
        foreach (var r in required)
            if (!covered.Contains(r)) fails.Add($"BESTIARY missing archetype {r}");

        // every bestiary blurb non-empty
        foreach (var b in Bestiary)
            if (string.IsNullOrWhiteSpace(b.Blurb)) fails.Add($"BESTIARY:{b.Cls} empty blurb");

        if (fails.Count == 0) return "CODEXTEST: PASS";
        return "CODEXTEST: FAIL\n  " + string.Join("\n  ", fails);
    }
}
