import re,io,sys
p='/home/user/wt/x1/src/Mission.cs'
s=open(p).read()
old = '''    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        return u;
    }'''
new = '''    // ─── PROGRAM RESONANCE X1 "THE EXCHANGE" — HOSTILE TOUGHNESS ─────────────────────────
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
    public const int HostileToughness = 4;

    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        int thp = hp + HostileToughness;
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = thp, MaxHp = thp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        return u;
    }'''
assert old in s, "anchor not found"
s = s.replace(old,new,1)
open(p,'w').write(s)
print("applied")
