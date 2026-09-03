using System;
using System.Collections.Generic;
using System.IO;

namespace Sightline;

// ═══════════════════════════════════════════════════════════════════════════════════════════
//  THE CUE MAP — one meaning, one cue.  PROGRAM CONTOUR, wave "cue-map" (THE BEAT part B).
//
//  The defect this file exists to make impossible: SIGHTLINE had ~130 Audio.Play call sites
//  and 28 cues, and the mapping between them was written one call site at a time by whoever
//  was in the file that day. The result was that a handful of cues each carried a fistful of
//  unrelated meanings — `over` was the overwatch chime AND the pod-wake CONTACT sting AND the
//  MARK ping AND the SUPPRESS ping AND the artillery charge whine ("a charge whine stand-in",
//  said the comment); `reload` was the mag change AND every ability AND every objective beat;
//  `turn` was all 27 banners including "VIP DOWN" and "AMBUSH!". A player cannot learn a
//  language whose words each mean six things, so the audio taught them nothing.
//
//  The fix is not more cues on their own — it is a TABLE. Every beat that a player is meant to
//  recognise is a `GameEvent`; `CueFor` is the only place that says which sound it makes; and
//  `SIGHTLINE_CUETEST` asserts the table is INJECTIVE, so a future wave physically cannot make
//  two meanings share a sound without the gate going red.
//
//  The second half is the BUS. `Audio.Play` picks the UI fader or the SFX fader from the cue's
//  category, which is right for a menu blip and wrong for the opponent's tells: an enemy
//  BRACED / OVERWATCH / RELOADING / ARTILLERY INCOMING telegraph was riding the UI fader, so a
//  player who turned UI volume down stopped hearing what the enemy was about to do. `PlayFoe`
//  is the same cue table on the SFX fader, and every `enemy:true` banner goes through it.
// ═══════════════════════════════════════════════════════════════════════════════════════════
public static partial class Audio
{
    /// A beat the player is meant to RECOGNISE. Not every Audio.Play has one — a menu blip and a
    /// footfall do not — but every beat that carries information does, and `CueFor` is the single
    /// place that decides what it sounds like.
    public enum GameEvent
    {
        Contact,          // a dormant pod wakes / the enemy has seen you
        Ambush,           // squad concealment breaks — the trap springs
        Reinforce,        // more of them arrive / the pressure clock ticks up / artillery is coming
        Explosion,        // grenade, barrel, siege strike
        Ability,          // a soldier engages a verb: RUN & GUN / BLITZ / STEADY / SLIPSTREAM / MARK / SUPPRESS
        Objective,        // objective progress: HACK / CHARGE SET / BEACON / INTEL
        Mend,             // STABILIZE / PATCH / REVIVE / a medic heal
        OverwatchSet,     // a watch is SET
        OverwatchFires,   // a watch ANSWERS
        // CONTROL PASSES TO THE PLAYER. The round changing hands in-mission, and the five
        // "the game advances a stage" confirms that already played this exact sound at a raw
        // `Audio.Play("turn")`: the draft launch, proceed-from-shop, an event resolving, the endless
        // offer, and a skirmish starting. P14 routed those five through the table rather than giving
        // them a cue of their own — they ARE one meaning, and the alternative was inventing an
        // eighth new sound that nobody in this sandbox can listen to. Naming them here is the point:
        // the table is now the only route to `turn`, and the census below enforces that.
        Turn,
        EnemyTurn,        // the round changes hands (to the opponent)
        ShopOk,           // a purchase went through
        ShopNo,           // a purchase was refused
        Reload,           // a magazine change (the literal one)
        // P14 THE UNVERIFIED — A BODY FALLS. It had no row, so the two banners that announce the
        // player's own losses — "VIP DOWN" and "SOLDIER DOWN - THEY HOLD FOR n" — borrowed
        // `Reinforce`, i.e. the REINFORCEMENT ALARM, "more of them are coming". Two loss beats
        // announced as an enemy arrival. It also owns the raw `Audio.Play("death")` sites now, so
        // the beat has exactly one route and the census can see it.
        Casualty,
    }

    /// The 13 events THE CUE MAP's injectivity gate is written against — the canonical set from
    /// the wave brief. `Reload` is asserted alongside them (it is distinct too); this array is
    /// what `SIGHTLINE_CUETEST` iterates so the contract is legible rather than implied.
    public static readonly GameEvent[] MappedEvents =
    {
        GameEvent.Contact, GameEvent.Ambush, GameEvent.Reinforce, GameEvent.Explosion,
        GameEvent.Ability, GameEvent.Objective, GameEvent.Mend,
        GameEvent.OverwatchSet, GameEvent.OverwatchFires,
        GameEvent.Turn, GameEvent.EnemyTurn, GameEvent.ShopOk, GameEvent.ShopNo,
        GameEvent.Reload, GameEvent.Casualty,
    };

    /// P14 — the "no cue" sentinel for ShowBanner: a banner whose beat ALREADY has a sound on the
    /// same frame. It is not a cue id and is never a registered recipe; ShowBanner treats it as
    /// silence. Used by VIP DOWN / SOLDIER DOWN, which fire on the exact frame the falling body
    /// plays `Casualty` — the fix for those two is to stop layering a SECOND, wrong sound on that
    /// beat, not to invent a third sound for it.
    public const string SilentCue = "";

    /// The events that are ONLY ever the OPPONENT acting.
    ///
    /// P14 THE UNVERIFIED — THIS LIST WAS WRONG, and wrong in the direction that makes a gate read
    /// stronger than it is. It used to include `Explosion` and `OverwatchFires` under the heading
    /// "only ever the OPPONENT acting", and both are routinely the PLAYER: every `Explosion` site
    /// in the tree is a grenade, an incendiary, a barrel or a siege strike and every one of them
    /// passes `foe:false` (Anim.cs GrenadeAnim/IncendiaryAnim, Game.cs's two blast sites), and
    /// `OverwatchFires` is one shared site that fires for whichever side owns the watcher. The
    /// ASSERTION they were carrying — "never on the UI fader" — is right for them; the REASON was
    /// not, and a reader checking the contract would have concluded the opposite of the truth.
    /// So the two jobs are split: this list is the OPPONENT's tells, and `NeverUiBus` below is the
    /// larger set that simply may not ride the UI fader. CUETEST asserts both.
    public static readonly GameEvent[] FoeTelegraphs =
    {
        GameEvent.Contact, GameEvent.Reinforce, GameEvent.EnemyTurn,
    };

    /// Every event that HAPPENS TO the player rather than being their own button answering back.
    /// None of these may resolve to a UI-category cue: a player who pulls the UI fader down to
    /// silence menu chrome must not also silence the opponent's tells, a grenade, a mend, or a
    /// watch answering. It is strictly larger than the old FoeTelegraphs list this leg used to run
    /// on, and CUETEST asserts FoeTelegraphs is a subset of it.
    ///
    /// The line is deliberately drawn at AGENCY, not at "does it happen on the board":
    /// `OverwatchSet`, `Reload`, `Ability` and `Objective` are all board beats, but each one is the
    /// confirmation of a click the player just made, which is what a UI fader is for — and the
    /// asymmetry is already shipped and deliberate, `Turn` being UI while `EnemyTurn` was moved off
    /// the UI bus by THE CUE MAP for exactly this reason. Whether that line is in the right place is
    /// a judgement about SOUND, and nobody in the sandbox can hear it; it is written down in
    /// docs/ROADMAP.md as an owner listen rather than settled here by assertion.
    public static readonly GameEvent[] NeverUiBus =
    {
        GameEvent.Contact, GameEvent.Ambush, GameEvent.Reinforce, GameEvent.Explosion,
        GameEvent.Mend, GameEvent.OverwatchFires, GameEvent.EnemyTurn, GameEvent.Casualty,
    };

    // ═══ THE TABLE — the ONE place that says what a beat sounds like ════════════════════════
    // Every entry is a beat a player is meant to RECOGNISE, and SIGHTLINE_CUETEST asserts the
    // right-hand column has no repeats. What each of these USED to play is in the comment: this
    // wave did not invent the meanings, it separated them.
    public static string CueFor(GameEvent e) => e switch
    {
        GameEvent.Contact        => "alert",       // was "over"   — the overwatch chime
        GameEvent.Ambush         => "ambush",      // was "turn"   — the round-change announce, twice
        GameEvent.Reinforce      => "alarm",       // was "turn"
        GameEvent.Explosion      => "boom",        // THE BEAT part A (was "crit"+"death" stacked)
        GameEvent.Ability        => "ability",     // was "reload" — and "over" for MARK / SUPPRESS
        GameEvent.Objective      => "tick",        // was "reload" — the mag-change cha-chk
        GameEvent.Mend           => "heal",        // THE BEAT part A (was "reload")
        GameEvent.OverwatchSet   => "over",        // the cue "over" was named for, and now its only job
        GameEvent.OverwatchFires => "react",       // THE BEAT part A (was "over", the same as SETTING one)
        GameEvent.Turn           => "turn",
        GameEvent.EnemyTurn      => "turn_enemy",  // was "turn"   — both sides sounded identical
        GameEvent.ShopOk         => "ui_ok",       // was "hit"    — a round landing on a body
        GameEvent.ShopNo         => "ui_no",       // was "miss"   — a round going past your ear
        GameEvent.Reload         => "reload",      // the literal magazine change, and nothing else
        GameEvent.Casualty       => "death",       // P14 — a body falls (this beat had no row)
        // P14 THE UNVERIFIED — this used to be `=> "select"`, the MENU BLIP. `GameEvent` and
        // `MappedEvents` are two hand-maintained lists, nothing checked one against the other, and
        // a member added to the enum and left out of both this switch and MappedEvents therefore
        // (a) played the menu confirm for a game beat and (b) was invisible to the injectivity
        // gate, because the gate iterates MappedEvents. Both halves are closed: CUETEST now
        // iterates Enum.GetValues and asserts MappedEvents is the WHOLE enum, and the fallback is
        // an id that is deliberately NOT a registered recipe — so an unmapped event fails the
        // gate's "is a registered recipe" leg loudly instead of blipping quietly.
        _                        => "?unmapped",
    };

    /// Play the cue for a game event. `foe` forces the SFX fader (see PlayFoe); `gainDb` is the
    /// same downward-only per-call trim Play takes (the incendiary's smaller boom is the one user).
    public static void Cue(GameEvent e, float panX = -1f, bool foe = false, float gainDb = 0f)
        => Play(CueFor(e), panX: panX, gainDb: gainDb, foe: foe);

    /// The mix bus a cue actually rides: "ui" (the UI fader) or "sfx". An OPPONENT's telegraph is
    /// never on the UI fader whatever the cue's own category says.
    public static string BusOf(string id, bool foe) => (!foe && IsUiCue(id)) ? "ui" : "sfx";

    /// Play an OPPONENT's telegraph — same cue table, always on the SFX fader.
    public static void PlayFoe(string id, float pitchVar = -1f, float panX = -1f, float gainDb = 0f)
        => Play(id, pitchVar, panX, gainDb, foe: true);

    /// The cue's mix CATEGORY (the audio budget's role band), exposed for the gate.
    public static string CategoryOf(string id) => CatOf(id);

    // ── the source census (leg c), REBUILT ──────────────────────────────────────────────────
    //  P14 THE UNVERIFIED. The old census counted two literal strings in ONE file:
    //  `Audio.Play("over")` and `Audio.Play("reload")` in src/Game.cs, against caps of 7 and 3.
    //  THE CUE MAP's own wave had removed every one of those call sites, so both terms read 0 and
    //  could only ever read 0: the drift the caps were meant to catch — a future site re-loading
    //  one meaning onto one cue — takes the form `Audio.Cue(GameEvent.X)` or a raw play of a
    //  DIFFERENT cue, neither of which those two greps can see. A census that cannot fail is worse
    //  than no census, because it is quoted as evidence that the table held.
    //
    //  The rule now is the one the table actually asserts, and it is checkable: THE TABLE OWNS ITS
    //  CUES. If a cue is the right-hand side of any row in `CueFor`, then every place that plays it
    //  must go through `Audio.Cue` / `Audio.CueFor` — never `Audio.Play("<that id>")`. Anything
    //  else is a second, invisible route to a mapped meaning, which is precisely how `over` came to
    //  answer to five things. Cues that are NOT in the table (`select`, `move`, `hunker`, `smoke`,
    //  `flash`, `win`, `lose`, `hit`, `death`) are untouched by this: one meaning at many sites is
    //  the opposite defect and is fine.
    //
    //  It scans ALL of src/*.cs, not one file. It is still a SOURCE SCAN and still says so: it runs
    //  from the repo tree (where qa-sweep.sh invokes every hook) and reports `n/a` from a published
    //  binary, where src/ does not ship. Legs (a) and (b) stand either way.
    public readonly struct CensusResult
    {
        public readonly bool HaveSrc;
        public readonly string[] Offenders;   // "file:Audio.Play(\"id\") xN"
        public readonly int TableUsers;       // Audio.Cue / Audio.CueFor call sites
        public readonly int FoeSites;         // PlayFoe + `foe: true`
        public CensusResult(bool have, string[] off, int users, int foe)
        { HaveSrc = have; Offenders = off; TableUsers = users; FoeSites = foe; }
    }

    /// Cues the table owns that a raw `Audio.Play("id")` may still use, BY NAME. Empty today, and
    /// it is meant to stay that way: an entry here is a declared exception, not a silent one.
    public static readonly string[] CensusExempt = { };

    public static CensusResult SourceCensus()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "src");
        if (!Directory.Exists(dir)) return new CensusResult(false, null, -1, -1);
        var owned = new HashSet<string>();
        foreach (GameEvent e in Enum.GetValues(typeof(GameEvent))) owned.Add(CueFor(e));
        foreach (var x in CensusExempt) owned.Remove(x);

        var offenders = new List<string>();
        int users = 0, foe = 0;
        foreach (var path in Directory.GetFiles(dir, "*.cs"))
        {
            string src = File.ReadAllText(path);
            string file = Path.GetFileName(path);
            if (file == "Audio.CueMap.cs") continue;      // the table's own file defines the ids
            foreach (var id in owned)
            {
                int n = Count(src, "Audio.Play(\"" + id + "\"");
                if (n > 0) offenders.Add($"{file}:Audio.Play(\"{id}\") x{n}");
            }
            users += Count(src, "Audio.Cue(") + Count(src, "Audio.CueFor(");
            foe   += Count(src, "Audio.PlayFoe(") + Count(src, "foe: true");
        }
        offenders.Sort();
        return new CensusResult(true, offenders.ToArray(), users, foe);
    }

    static int Count(string hay, string needle)
    {
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
