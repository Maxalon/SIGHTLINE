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
        Turn,             // the round changes hands (to the player)
        EnemyTurn,        // the round changes hands (to the opponent)
        ShopOk,           // a purchase went through
        ShopNo,           // a purchase was refused
        Reload,           // a magazine change (the literal one)
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
        GameEvent.Reload,
    };

    /// The events that are ONLY ever the OPPONENT acting. Their cue may never be a UI-bus cue:
    /// a player who pulls the UI fader down is turning off menu chrome, not the enemy's tells.
    public static readonly GameEvent[] FoeTelegraphs =
    {
        GameEvent.Contact, GameEvent.Reinforce, GameEvent.EnemyTurn,
        GameEvent.OverwatchFires, GameEvent.Explosion,
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
        _                        => "select",
    };

    /// Play the cue for a game event. `foe` forces the SFX fader (see PlayFoe).
    public static void Cue(GameEvent e, float panX = -1f, bool foe = false)
        => Play(CueFor(e), panX: panX, foe: foe);

    /// The mix bus a cue actually rides: "ui" (the UI fader) or "sfx". An OPPONENT's telegraph is
    /// never on the UI fader whatever the cue's own category says.
    public static string BusOf(string id, bool foe) => (!foe && IsUiCue(id)) ? "ui" : "sfx";

    /// Play an OPPONENT's telegraph — same cue table, always on the SFX fader.
    public static void PlayFoe(string id, float pitchVar = -1f, float panX = -1f, float gainDb = 0f)
        => Play(id, pitchVar, panX, gainDb, foe: true);

    /// The cue's mix CATEGORY (the audio budget's role band), exposed for the gate.
    public static string CategoryOf(string id) => CatOf(id);

    // ── the source census (leg c) ────────────────────────────────────────────────────────────
    // "How many meanings does one cue carry" is ultimately a question about the CALL SITES, and
    // the honest way to answer it is to count them. This reads src/Game.cs relative to the
    // working directory, so it is meaningful from the repo tree (which is where qa-sweep.sh runs
    // every hook) and reports `n/a` from a published binary, where src/ does not ship. It is a
    // source scan and is labelled as one — it is not pretending to be a runtime assertion.
    public static (bool ok, int over, int reload, int foe) SourceCensus()
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), "src", "Game.cs");
        if (!File.Exists(path)) return (false, -1, -1, -1);
        string src = File.ReadAllText(path);
        return (true, Count(src, "Audio.Play(\"over\")"), Count(src, "Audio.Play(\"reload\")"),
                Count(src, "Audio.PlayFoe(") + Count(src, "foe: true"));
    }
    static int Count(string hay, string needle)
    {
        int n = 0, i = 0;
        while ((i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
