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
            Category("FIELD CRAFT", FieldCraftEntries()),   // W11: the RULES tab, first — read this, win fights
            Category("VERBS & KEYS", VerbEntries()),        // W5: every action-bar verb + its hotkey
            Enemies(),
            Category("FACTIONS",    FactionEntries()),      // C1 (VOICE): who you are actually fighting
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
        // SIGNAL W8 — morale contested: the banner anchor + the objective keeper.
        ("SIGNIFER", "WARBRINGER", "Standard-bearer — pods near its banner cannot rout and rally faster. Kill the banner to break their nerve."),
        ("SEXTON",  "CUSTODIAN",  "Objective keeper — walks to the terminal or a blown charge and undoes one step of your progress each turn. Screen it out or shoot it first."),
        // FUL-8 — movement contested: the Wardens lane-holder (the enemy-side mirror of BRACE [B]).
        ("SARISSA", "PIKEMAN",   "Lane-holder — plants a braced cone over a movement lane and STAGGERS the first soldier through: reduced damage, but your action is denied. It is exactly your own BRACE. Break its watch, go around the cone, or feed it a cheap step first."),
        // SIGNAL W5: every named boss (BREAKER/BULWARK/WARDEN mid-bosses; WARLORD/SIEGELORD/
        // SPYMASTER finales) shares Cls "ELITE" — one bestiary row covers the family, and the
        // blurb now names the faction signatures so the manual matches the new climax kits.
        ("WARLORD", "ELITE",     "Named boss / mid-boss — tough, carries grenades, and rages at low HP. Faction champions add a signature: a killing frenzy, a frontal shield arc, or telegraphed siege strikes."),
    };

    static CodexCategory Enemies()
    {
        var e = new List<CodexEntry>();
        foreach (var b in Bestiary)
            e.Add(new CodexEntry { Title = b.Name, Code = b.Cls, Desc = b.Blurb, Glyph = b.Cls });
        return Category("ENEMIES", e, glyph: true);
    }

    // ---------------- bestiary accessors (W11: teach it where it's played) ----------------
    // The HUD reads these to ID a hovered enemy (tooltip), banner a first contact, and name the
    // run-killer on the lose card — one source of truth, so the in-fight text can't drift from
    // the manual. All keyed by the archetype string (Unit.Cls).

    /// The archetype's in-mission callsign ("SNIPER" -> "VIPER"). Falls back to the class string.
    public static string NameFor(string cls)
    {
        foreach (var b in Bestiary) if (b.Cls == cls) return b.Name;
        return cls ?? "";
    }

    /// The full bestiary blurb for an archetype ("" for an unknown class).
    public static string BlurbFor(string cls)
    {
        foreach (var b in Bestiary) if (b.Cls == cls) return b.Blurb;
        return "";
    }

    /// The blurb's FIRST clause (up to the first '.' or ';') — the terse in-fight ID line.
    public static string BlurbClause(string cls)
    {
        string b = BlurbFor(cls);
        if (string.IsNullOrEmpty(b)) return "";
        int cut = b.IndexOfAny(new[] { '.', ';' });
        return cut > 0 ? b.Substring(0, cut) : b;
    }

    /// The blurb's counterplay clause — the LAST sentence (most blurbs end on the "what to do
    /// about it" beat). A single-sentence blurb returns whole, minus the trailing period.
    public static string TipFor(string cls)
    {
        string b = BlurbFor(cls);
        if (string.IsNullOrEmpty(b)) return "";
        var parts = b.TrimEnd('.').Split('.');
        string tip = parts[parts.Length - 1].Trim();
        return tip.Length > 0 ? tip : b;
    }

    // ---------------- FIELD CRAFT (W11: the rules tab) ----------------
    // Every doctrine of the fight in one place, in the order a new player needs them. HARD RULE:
    // each number below is the REAL constant the combat code applies — interpolated from the
    // public consts where one exists (Combat.*, Game.*), literal only where the source is inline
    // (cover Defense 20/40 in Grid.CoverInfo; the exposed +18 crit in Combat.ComputeOdds). A wrong
    // number in a rules manual is worse than none; keep these honest when tuning.
    static List<CodexEntry> FieldCraftEntries()
    {
        var e = new List<CodexEntry>();
        void Add(string title, string code, string desc)
            => e.Add(new CodexEntry { Title = title, Code = code, Desc = desc });

        Add("COVER", "-20/-40",
            "End every move beside a block. LOW cover costs the attacker 20 aim; HIGH costs 40. " +
            "In the open you read EXPOSED: +18 crit against you.");
        Add("HUNKER", "DIG IN",
            "Spend the rest of a turn digging in: a further -25 aim against you, and you cannot be crit. " +
            "Strongest layered onto cover, but even in the open it blunts aim and voids the crit.");
        Add("FLANKING", "NO COVER",
            "Cover only faces the shot. Fire from a side the block does not face and the target is FLANKED: " +
            "its cover is void and the EXPOSED +18 crit applies. Your own sides obey the same rule.");
        Add("DIAGONALS", "CORNERS",
            "A diagonal shot at range past ONE facing block is HALF cover (-10 low / -20 high). A TRUE corner " +
            "(blocks on BOTH facing sides) holds full cover. Adjacent, a diagonal slips a single corner entirely: a flank.");
        Add("HIGH GROUND", $"+{Combat.HighGroundAim}/+{Combat.HighGroundCrit}",
            $"Fire from raised terrain for +{Combat.HighGroundAim} aim and +{Combat.HighGroundCrit} crit — and you see OVER " +
            "the target's LOW cover. A commanding two-tier drop sees over HIGH cover too.");
        Add("CONCEALMENT", "AMBUSH",
            $"The squad starts hidden; pods hold and cannot escalate by sight. Spotted at range a pod turns SUSPICIOUS (!), then ALERT. " +
            $"Stepping within {Game.BaseRevealRange} tiles of an alert foe (heat's SHORT FUSE tightens this by one) — or any loud act — breaks stealth. " +
            $"The first shot from hiding is an AMBUSH: +{Combat.AmbushAim} aim, +{Combat.AmbushCrit} crit.");
        Add("REACTIONS", "OW/FOCUS/BRACE",
            $"Three ways to hold a lane. OVERWATCH fires on the first mover in sight at -10 aim. FOCUS narrows to a " +
            $"90-degree cone at +{Combat.FocusOwAim} aim inside it — blind outside. BRACE trades the kill for tempo: " +
            "half damage, no crit, but a hit STAGGERS the mover — its actions this turn are denied.");
        Add("POD MORALE", "ROUT",
            $"Cut a pod to half its spawn strength and the survivors BREAK: for {Game.RoutDuration} turns they flee, " +
            "drop overwatch and shoot wild, then rally. Finishing one pod beats winging two.");
        Add("PRESSURE", "CLOCK",
            $"On camp-friendly objectives (Eliminate / Hack / Decapitate) turtling is taxed: {Game.PressureGrace} grace turns, " +
            $"then enemy aim climbs +{Game.PressureAimPerRung} per rung (+{Game.PressureAimPerRung + 1} at heat 4+) every {Game.PressureStep} turns " +
            $"(max {Game.PressureMax} rungs), with reinforcements from rung 2. Advance.");
        Add("GRAZE", "SAFETY NET",
            $"A shot that misses by {Combat.GrazeBand} or less GRAZES: minimum damage, no crit — never nothing. " +
            $"A {Combat.GrazeMinMiss}% true-miss window always remains. Each clean miss also banks " +
            $"+{Combat.StreakBonusPerMiss} aim (STEADYING, max +{Combat.MaxStreakBonus}) until you connect — " +
            "it is already counted in the HIT% you see.");
        Add("TEMPO", "FIRE & MOVE",
            "Firing costs 1 action and does NOT end the turn — shoot, then reposition, in either order. " +
            $"One full-aim shot per soldier per turn: a second is RUSHED at {Game.SnapAim} aim.");
        Add("SUPPRESSION", $"-{Combat.SuppressAim} AIM",
            $"Weight of fire pins a target: -{Combat.SuppressAim} aim, and a PINNED foe cannot dash. " +
            $"A sharpshooter's MARK is the mirror: the whole squad gains +{Combat.MarkAim} aim against the painted foe.");
        // W5 (audit newplayer-7): SHOVE and the four UTILITY ITEMS appeared NOWHERE in this file.
        // Their only explanation in the entire product was a 9-second just-in-time card that
        // Game.UpdateFieldTips burns permanently the moment it shows ("one-shot: burned the moment
        // it shows") — so a player who was mid-thought, alt-tabbed, or simply reading the board
        // when it fired lost that verb's only teaching for the life of the profile. Both are
        // once-per-turn positional verbs with real tactical weight. Numbers interpolated from the
        // real constants, like every other row here.
        Add("SHOVE", $"{Game.ShoveReach} TILES",
            $"Push an adjacent hostile one tile straight back, from up to {Game.ShoveReach} tiles' reach. " +
            $"It BREAKS the target's overwatch and can strip its cover — shove a foe out from behind a block " +
            $"and the lane opens for the rest of the squad. Blocked (a wall, another body) it deals " +
            $"{Combat.ShoveCollisionDamage} damage instead, plus {Combat.ShoveRammedDamage} to whatever it was " +
            "rammed into. 1 action, does NOT end your turn, once per soldier per turn.");
        Add("UTILITY ITEMS", "1 / MISSION",
            "Every class carries one throwable with a SINGLE charge per mission — the decision is when, not " +
            "whether. SMOKE (ranger) blocks line of sight and overwatch through the cloud for a few turns. " +
            "FLASH (assault) disorients everyone in the blast: -aim, and no overwatch next turn. " +
            "INCENDIARY (sharpshooter) sets a 3x3 fire field that denies ground, ignites foes and cooks barrels. " +
            "BARRICADE (gunner) drops low cover on an empty tile — cover where the map gave you none.");
        // FUL-6: the two universal positioning verbs get a rules row (they had none), incl. the
        // reworked FIELD DRILLS drill effect so the boon's copy is anchored in the rules tab.
        Add("DRAG & VAULT", "FIELD CRAFT",
            "Two universal 1-action verbs, once per soldier per turn each: DRAG pulls an ally within reach one tile " +
            "toward you (haul a wounded mate out of a lane); VAULT leaps an adjacent cover block to the floor beyond. " +
            "Neither ends the turn. The FIELD DRILLS boon makes either DRILL the soldier: +1 tile of movement that turn.");

        return e;
    }

    // ---------------- VERBS & KEYS (W5: the controls reference the game never had) ----------
    // Generated from Hud.VerbTable + Hud.VerbHelp — i.e. from the SAME switch the action bar's
    // hover tooltip reads — so a verb's help and its manual entry cannot drift apart. Before W5
    // there was no controls or keybinding reference anywhere in the product: `grep -rE
    // "CONTROLS|KEYBIND"` over src/ returned nothing, and the only route to a verb's explanation
    // was discovering that action-bar buttons have mouse-hover help.
    static List<CodexEntry> VerbEntries()
    {
        var e = new List<CodexEntry>();
        foreach (var v in Hud.VerbTable)
        {
            string help = Hud.VerbHelp(v.Id);
            if (string.IsNullOrWhiteSpace(help)) continue;
            e.Add(new CodexEntry { Title = v.Label, Code = "[" + v.Key + "]", Desc = help });
        }
        // The non-verb bindings, which live nowhere else at all.
        e.Add(new CodexEntry { Title = "SELECTING & MOVING", Code = "CLICK",
            Desc = "Click a soldier to select; [Tab] cycles. Click inside the CYAN OUTLINE to move - the corner " +
                   "ticks mark each reachable tile, and the DASHED outer ring costs both actions. Arrows / WASD " +
                   "drive a keyboard cursor and [Space] acts on it. [Enter] ends the turn." });
        e.Add(new CodexEntry { Title = "CAMERA", Code = "WHEEL",
            Desc = "Mouse wheel zooms, middle-drag pans, [C] resets. AUTO-CAM (pause menu) follows the action on " +
                   "its own." });
        e.Add(new CodexEntry { Title = "THE REST", Code = "GLOBAL",
            Desc = "[Esc] cancels an aim / targeting mode, or opens the pause menu. [K] opens this manual from " +
                   "anywhere. [T] writes a custom tag on the selected soldier. [V] shows every verb while the " +
                   "onboarding is staging the bar. [M] mutes, [F11] is fullscreen, [F2] cycles animation speed, " +
                   "[Q] quits from the pause card or the main menu." });
        return e;
    }

    // ---------------- FACTIONS (3 + the unaligned force) ----------------
    // RESONANCE C1 (VOICE): the three factions had mechanics (a combat warp, a gated roster, a
    // named capstone boss and a counter-prep item) and no identity anywhere in the game. The
    // dossier text lives in src/Voice.cs so the briefing card and this tab read ONE source, and
    // each dossier's FIELD RULE line interpolates the real Combat constant — the fiction cannot
    // drift from the rule. Faction.None is documented too: "no colours" is a real encounter type.
    static List<CodexEntry> FactionEntries()
    {
        var e = new List<CodexEntry>();
        // READING ORDER, not enum order: the three real opponents lead, and the unaligned
        // "no colours" force closes the tab. Faction.None is ordinal 0, so iterating the enum
        // would open the dossier tab on the faction that is defined by not being one.
        foreach (Faction f in new[] { Faction.Syndicate, Faction.Legion, Faction.Wardens, Faction.None })
            e.Add(new CodexEntry
            {
                Title = Voice.FactionEpithet(f),
                Code = f == Faction.None ? "UNALIGNED" : Run.FinaleBossName(f).ToUpperInvariant(),
                Desc = Voice.FactionDossier(f),
            });
        return e;
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

        // W11: the battlefield states that live OUTSIDE the StatusKind enum (unit flags, pod morale,
        // squad stealth) — plain CodexEntry rows, NOT enum members (StatusKind is persisted by
        // ordinal and stays append-only). Every banner/pop-text word a fight can stamp on a unit
        // is now findable here. Magnitudes are the real constants.
        e.Add(new CodexEntry { Title = "ROUTED", Code = "MORALE",
            Desc = $"Its pod broke at half strength — in a pod of 3 the first kill sets the survivors WAVERING and the second breaks them: flees toward its own edge, drops overwatch and shoots wild for {Game.RoutDuration} turns, then rallies." });
        // FUL-6: linked activation is a coded battlefield state the player must be able to look up.
        e.Add(new CodexEntry { Title = "LINKED ALERTS", Code = "HEARD THE GUNS",
            Desc = $"Gunfire carries: waking a pod alerts the nearest dormant pod within earshot ({Game.LinkRange} tiles, missions 3+); it arrives one turn later, without the ambush scatter. You always get the warning. Counters: kill the woken pod inside the warning turn and set a line (BRACE/overwatch/frag) for the second; pre-frag the telegraphed pod (it lands clumped); or open the fight from a lane where no second pod sits within earshot." });
        // FUL-7: the bleed-out window is a coded battlefield state — the player must be able to
        // look up the whole rescue kit. Magnitudes are the real constants.
        e.Add(new CodexEntry { Title = "DOWN (BLEEDING OUT)", Code = "DOWN",
            Desc = $"Lethal damage drops a soldier for {Game.DownedTimerTurns} turns instead of killing them. STABILIZE (any adjacent soldier, 1 action) freezes the timer; a CORPSMAN's PATCH gets them back up; DRAG or haul them to extraction. Blasts and fire finish the job - and nobody survives going down twice in one mission." });
        e.Add(new CodexEntry { Title = "STAGGERED", Code = "BRACE",
            Desc = "Interrupted by a BRACE reaction: remaining actions this turn are denied and any held overwatch drops." });
        e.Add(new CodexEntry { Title = "SUPPRESSED", Code = $"-{Combat.SuppressAim} AIM",
            Desc = $"Pinned by weight of fire: -{Combat.SuppressAim} aim while the fire lasts." });
        e.Add(new CodexEntry { Title = "PINNED", Code = "NO DASH",
            Desc = "Held by a gunner's suppressing fire: takes the SUPPRESSED aim penalty and cannot dash (single moves only) until it wears off." });
        e.Add(new CodexEntry { Title = "MARKED", Code = $"+{Combat.MarkAim} AIM",
            Desc = $"Painted by a sharpshooter: every squad member gains +{Combat.MarkAim} aim against this foe until the marker's next turn (a HEADHUNTER's mark adds +{Combat.HeadhunterMarkCrit} crit)." });
        e.Add(new CodexEntry { Title = "CONCEALED", Code = "HIDDEN",
            Desc = $"The squad is unseen; pods cannot escalate by sight. Broken by any loud act or stepping within {Game.BaseRevealRange} tiles (one less under heat's SHORT FUSE) of an alert foe — the breaking shot is an AMBUSH (+{Combat.AmbushAim} aim, +{Combat.AmbushCrit} crit)." });
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
            "WARBRINGER","CUSTODIAN",   // W8: banner anchor + objective keeper
            "PIKEMAN",                  // FUL-8: the lane-holder (enemy-side BRACE mirror)
        };
        var covered = new HashSet<string>();
        foreach (var b in Bestiary) covered.Add(b.Cls);
        foreach (var r in required)
            if (!covered.Contains(r)) fails.Add($"BESTIARY missing archetype {r}");

        // every bestiary blurb non-empty
        foreach (var b in Bestiary)
            if (string.IsNullOrWhiteSpace(b.Blurb)) fails.Add($"BESTIARY:{b.Cls} empty blurb");

        // ── W5 (audit newplayer-7): EVERY ACTION-BAR VERB HAS A PERMANENT HOME ────────────────
        // Three verbs were taught only by a 9-second just-in-time card that Game.UpdateFieldTips
        // burns the instant it shows, and had no entry anywhere in this file — `shove` and `item`
        // failed this assertion before W5. The Field Manual is the backstop for every verb the
        // tips teach, so the manual, not the timing of a card, decides whether the player can
        // ever learn a verb.
        {
            var verbTab = Category("VERBS & KEYS", VerbEntries());
            var titles = new HashSet<string>();
            foreach (var en in verbTab.Entries) titles.Add(en.Title);
            foreach (var v in Hud.VerbTable)
            {
                if (string.IsNullOrWhiteSpace(Hud.VerbHelp(v.Id))) { fails.Add($"VERB:{v.Id} has no help text"); continue; }
                if (!titles.Contains(v.Label)) fails.Add($"VERB:{v.Id} missing from VERBS & KEYS");
                if (string.IsNullOrWhiteSpace(v.Key)) fails.Add($"VERB:{v.Id} has no hotkey");
            }
            // and the two rules rows the tips used to be the only home for
            var craft = FieldCraftEntries();
            bool hasShove = false, hasItems = false;
            foreach (var en in craft)
            {
                if (en.Title == "SHOVE") hasShove = true;
                if (en.Title == "UTILITY ITEMS") hasItems = true;
            }
            if (!hasShove) fails.Add("FIELD CRAFT missing SHOVE");
            if (!hasItems) fails.Add("FIELD CRAFT missing UTILITY ITEMS");
        }

        // W11: the HUD-facing accessors resolve for every archetype (enemy-ID tooltip, NEW CONTACT
        // banner, lose-card cause line all read these — an empty return would draw a blank ID).
        foreach (var r in required)
        {
            if (string.IsNullOrWhiteSpace(BlurbFor(r)))     fails.Add($"BLURBFOR:{r} empty");
            if (string.IsNullOrWhiteSpace(NameFor(r)))      fails.Add($"NAMEFOR:{r} empty");
            if (string.IsNullOrWhiteSpace(BlurbClause(r)))  fails.Add($"BLURBCLAUSE:{r} empty");
            if (string.IsNullOrWhiteSpace(TipFor(r)))       fails.Add($"TIPFOR:{r} empty");
        }

        // W11: FIELD CRAFT rules tab — present, first, and fully populated (title+desc per row).
        var fc = FieldCraftEntries();
        if (fc.Count < 12) fails.Add($"FIELDCRAFT only {fc.Count} entries (want >= 12)");
        foreach (var en in fc) Chk("FIELDCRAFT", en.Title ?? "?", en.Title, en.Desc);
        var cats0 = Build();
        if (cats0.Count == 0 || cats0[0].Name != "FIELD CRAFT") fails.Add("FIELD CRAFT is not the first codex category");

        // C1: the FACTIONS tab exists, is complete, and covers every Faction member (incl. None).
        var facCat = Build().Find(c => c.Name == "FACTIONS");
        if (facCat == null) fails.Add("FACTIONS category missing from the codex");
        else
        {
            if (facCat.Entries.Count != Enum.GetValues(typeof(Faction)).Length)
                fails.Add($"FACTIONS has {facCat.Entries.Count} entries, Faction has {Enum.GetValues(typeof(Faction)).Length} members");
            foreach (var en in facCat.Entries) Chk("FACTION", en.Title ?? "?", en.Title, en.Desc);
        }

        // W11: the STATUS tab documents the out-of-enum battlefield states too.
        var statusRows = StatusEntries();
        foreach (var want in new[] { "ROUTED", "STAGGERED", "SUPPRESSED", "PINNED", "MARKED", "CONCEALED",
                                     "LINKED ALERTS",     // FUL-6: the heard-the-guns telegraph row
                                     "DOWN (BLEEDING OUT)" })   // FUL-7: the bleed-out window + rescue kit
            if (!statusRows.Exists(r => r.Title == want && !string.IsNullOrWhiteSpace(r.Desc)))
                fails.Add($"STATUS missing W11 row {want}");

        if (fails.Count == 0) return "CODEXTEST: PASS";
        return "CODEXTEST: FAIL\n  " + string.Join("\n  ", fails);
    }
}
