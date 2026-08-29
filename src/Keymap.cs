using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Raylib_cs;

namespace Sightline;

/// PROGRAM RESONANCE W6 — THE KEY MAP.
///
/// One table that owns every rebindable key in the game, and the single place any of them is
/// read. Before W6 there were ~40 loose `Raylib.IsKeyPressed(KeyboardKey.X)` sites spread through
/// Game.cs, and the consequence was measured, not theoretical: `F` was bound to BOTH fullscreen
/// (a global, read every phase) and FOCUS overwatch (a player-turn verb) for the whole life of the
/// project. `IsKeyPressed` is true for both reads in the same frame, so one press spent a soldier's
/// action AND toggled the window. Nothing could have caught that, because nothing knew the two
/// reads existed. Now they are ROWS IN A TABLE, each declaring the SCOPE it is live in, and
/// `Keymap.SelfCheck()` fails the build's self-test the moment two rows in overlapping scopes claim
/// the same key. The class of bug is closed by construction, not by vigilance.
///
/// THREE THINGS THIS FILE GUARANTEES, in priority order:
///
///  1. THE PLAYER CAN ALWAYS GET BACK. `Escape` is a FIXED binding: it is not in the rebindable
///     table, it is refused as a target for any rebind, and it is the key that cancels targeting
///     and opens the pause menu (Raylib's own exit-key behaviour is deliberately disabled — see
///     Program.cs `SetExitKey(KeyboardKey.Null)` — so Escape is OUR key, not the window manager's).
///     From the pause menu, CONTROLS -> RESET DEFAULTS is always one click away. A remap can
///     therefore never require a settings-file edit to recover from. `Escape` also cancels an
///     in-progress key capture, so even the rebind UI cannot trap you.
///
///  2. CONFLICTS ARE SURFACED, NEVER SILENT. `Set` refuses a key already claimed in an overlapping
///     scope and says which action holds it. `Repair` re-validates a whole loaded map (a
///     hand-edited settings file included) and drops what does not fit rather than shipping a
///     double-bind. `Conflicts()` reports what is left for the UI to draw.
///
///  3. CONTEXTS ARE REAL, SO CONFLICTS ARE TRUE. `K` means FIELD MANUAL on the pause overlay and
///     nothing at all during the player turn; `S` means SKIRMISH on the intro and nothing in a
///     mission. Those are separate namespaces and flattening them would manufacture conflicts that
///     do not exist. Each row declares a scope MASK, and two rows only conflict when their masks
///     intersect — which is exactly the condition under which both reads run in the same frame.
///
/// PERSISTENCE: a compact `id=KeyName` string carried in `display.json` (Display.Dto.Keys). Keys
/// are stored by ENUM NAME, not ordinal — no new persisted enum, nothing for the APPEND-ONLY
/// fingerprint guard in SaveGame to worry about, and a settings file a human can read. Only
/// OVERRIDES are written, so a future change to a default reaches every player who never touched
/// that row.
///
/// HEADLESS: `Display.Init(false)` never Loads, so the harness always runs the shipped defaults and
/// every `Keymap.Pressed` is exactly the `Raylib.IsKeyPressed` call it replaced. No new
/// `Raylib.GetTime()` reads, no allocation per frame, nothing for the balance flywheel to see.
public static class Keymap
{
    // ---- scopes -------------------------------------------------------------------------------
    // A scope is "a set of frames in which this read runs". Two bindings can share a key iff their
    // scopes are disjoint, because then the two reads never happen on the same frame.
    public const int ScopeMenu    = 1;   // Intro / Draft / Barracks / WarRoom / Codex / SkirmishSetup / AudioCheck / Win / Lose
    public const int ScopeMission = 2;   // PlayerTurn / EnemyTurn, NOT paused
    public const int ScopePause   = 4;   // the pause overlay (swallows mission input while it is up)
    public const int ScopeAll     = ScopeMenu | ScopeMission | ScopePause;

    public static string ScopeName(int s) =>
        s == ScopeAll ? "EVERYWHERE"
        : s == ScopeMission ? "IN MISSION"
        : s == ScopePause ? "PAUSE MENU"
        : s == ScopeMenu ? "MENUS"
        : "MIXED";

    // ---- stable action ids ---------------------------------------------------------------------
    // `const string` rather than bare literals so a typo at a call site is a COMPILE error, not a
    // key that silently stops working. The verb ids deliberately match the action-bar button ids in
    // Hud.DrawActionButtons / Game.DoAction, so the bar's key hint is just Keymap.Label(b.Id).
    /// The one FIXED row: Escape. Named here so call sites read it from the table like everything
    /// else, but it is `Fixed` and every rebind onto it is refused — see guarantee #1 above.
    public const string Menu = "menu";
    public const string Mute = "mute", Fullscreen = "fullscreen", AnimSpeed = "animspeed";
    public const string EndTurn = "endturn", Cycle = "cycle", Act = "act", CamReset = "camreset";
    public const string CursorUp = "cursorup", CursorDown = "cursordown";
    public const string CursorLeft = "cursorleft", CursorRight = "cursorright";
    public const string Shoot = "shoot", Overwatch = "overwatch", FocusOw = "focusow", Brace = "brace";
    public const string Hunker = "hunker", Grenade = "grenade", Ability = "ability", Item = "item";
    public const string Drag = "drag", Shove = "shove", Vault = "vault";
    public const string Hack = "hack", Stabilize = "stabilize", Beacon = "beacon", Extract = "extract";
    public const string Reload = "reload", Tag = "tag", ShowAll = "showall", RestartDrill = "restartdrill";
    public const string PauseCodex = "pausecodex", PauseAudio = "pauseaudio", PauseControls = "pausecontrols";

    /// One rebindable (or fixed) control.
    public sealed class Bind
    {
        public string Id;            // stable, persisted
        public string Label;         // shown in the CONTROLS screen
        public string Group;         // section header
        public int    Scope;         // ScopeMenu | ScopeMission | ScopePause
        public KeyboardKey Def;      // shipped default
        public KeyboardKey Key;      // current
        public bool   Fixed;         // not rebindable (Escape, and the cursor's arrow alternates)
        public bool   Hidden;        // fixed AND not worth its own row (summarised in the footer)
        public string Note;          // one-line explanation for the CONTROLS screen
    }

    // ---- the table ------------------------------------------------------------------------------
    // ORDER IS THE SCREEN ORDER. Groups must stay contiguous.
    static readonly Bind[] Table = BuildTable();

    static Bind B(string id, string label, string group, int scope, KeyboardKey def, string note = "")
        => new Bind { Id = id, Label = label, Group = group, Scope = scope, Def = def, Key = def, Note = note };

    static Bind F(string id, string label, string group, int scope, KeyboardKey def, bool hidden, string note = "")
        => new Bind { Id = id, Label = label, Group = group, Scope = scope, Def = def, Key = def,
                      Fixed = true, Hidden = hidden, Note = note };

    static Bind[] BuildTable()
    {
        var t = new List<Bind>
        {
            // ---- SYSTEM: live in every phase, so these are the rows that can collide with anything ----
            F(Menu,           "CANCEL / MENU",  "SYSTEM", ScopeAll, KeyboardKey.Escape, false,
                 "Reserved. Cancels targeting, opens the pause menu, backs out of any screen."),
            B(Mute,           "MUTE AUDIO",     "SYSTEM", ScopeAll, KeyboardKey.M),
            B(Fullscreen,     "FULLSCREEN",     "SYSTEM", ScopeAll, KeyboardKey.F11),
            B(AnimSpeed,      "ANIM SPEED",     "SYSTEM", ScopeAll, KeyboardKey.F2),

            // ---- MISSION: the player turn's verbs + the board cursor ----
            B(Shoot,          "FIRE",           "MISSION", ScopeMission, KeyboardKey.One),
            B(Overwatch,      "OVERWATCH",      "MISSION", ScopeMission, KeyboardKey.Two),
            B(FocusOw,        "FOCUS (CONE)",   "MISSION", ScopeMission, KeyboardKey.F),
            B(Brace,          "BRACE",          "MISSION", ScopeMission, KeyboardKey.B),
            B(Hunker,         "HUNKER",         "MISSION", ScopeMission, KeyboardKey.Three),
            B(Grenade,        "GRENADE",        "MISSION", ScopeMission, KeyboardKey.Four),
            B(Ability,        "ABILITY",        "MISSION", ScopeMission, KeyboardKey.Five),
            B(Item,           "ITEM",           "MISSION", ScopeMission, KeyboardKey.Six),
            B(Drag,           "DRAG",           "MISSION", ScopeMission, KeyboardKey.Seven),
            B(Shove,          "SHOVE",          "MISSION", ScopeMission, KeyboardKey.Eight),
            B(Vault,          "VAULT",          "MISSION", ScopeMission, KeyboardKey.Nine),
            B(Hack,           "HACK / PLANT",   "MISSION", ScopeMission, KeyboardKey.H),
            B(Stabilize,      "STABILIZE",      "MISSION", ScopeMission, KeyboardKey.E),
            B(Beacon,         "BEACON",         "MISSION", ScopeMission, KeyboardKey.G),
            B(Extract,        "EXTRACT",        "MISSION", ScopeMission, KeyboardKey.X),
            B(Reload,         "RELOAD",         "MISSION", ScopeMission, KeyboardKey.R),
            B(Tag,            "RENAME SOLDIER", "MISSION", ScopeMission, KeyboardKey.T),
            B(ShowAll,        "SHOW ALL VERBS", "MISSION", ScopeMission, KeyboardKey.V),
            B(RestartDrill,   "RESTART DRILL",  "MISSION", ScopeMission, KeyboardKey.P,
                 "Training op only."),
            B(EndTurn,        "END TURN",       "MISSION", ScopeMission, KeyboardKey.Enter),
            B(Cycle,          "NEXT SOLDIER",   "MISSION", ScopeMission, KeyboardKey.Tab),
            B(Act,            "ACT ON CURSOR",  "MISSION", ScopeMission, KeyboardKey.Space),
            B(CamReset,       "RESET CAMERA",   "MISSION", ScopeMission, KeyboardKey.C),
            B(CursorUp,       "CURSOR UP",      "MISSION", ScopeMission, KeyboardKey.W),
            B(CursorDown,     "CURSOR DOWN",    "MISSION", ScopeMission, KeyboardKey.S),
            B(CursorLeft,     "CURSOR LEFT",    "MISSION", ScopeMission, KeyboardKey.A),
            B(CursorRight,    "CURSOR RIGHT",   "MISSION", ScopeMission, KeyboardKey.D),

            // ---- PAUSE MENU: only live while the pause overlay is up ----
            // These two had a key HINT on their buttons ("K", "U") since the day they shipped, and
            // the hint was a lie: HandlePauseMenu was mouse-only, so neither key did anything while
            // paused. Routing them through the table both makes the advertised key work and gives
            // the letters a scope, so they no longer look like conflicts with anything.
            B(PauseCodex,     "FIELD MANUAL",   "PAUSE MENU", ScopePause, KeyboardKey.K),
            B(PauseAudio,     "AUDIO CHECK",    "PAUSE MENU", ScopePause, KeyboardKey.U),
            B(PauseControls,  "CONTROLS",       "PAUSE MENU", ScopePause, KeyboardKey.O),
        };

        // ---- fixed reservations -------------------------------------------------------------
        // Not rows the player edits; they exist so the conflict scan knows these keys are TAKEN in
        // the scope they are taken in. Without them a rebind could quietly shadow a menu shortcut
        // or the arrow-key cursor fallback and the surface would report "no conflicts".
        // The arrows are a deliberate ALWAYS-AVAILABLE alternate for the board cursor: whatever the
        // player does to WASD, the board stays drivable.
        t.Add(F("alt.up",    "CURSOR UP (ALT)",    "MISSION", ScopeMission, KeyboardKey.Up,    true));
        t.Add(F("alt.down",  "CURSOR DOWN (ALT)",  "MISSION", ScopeMission, KeyboardKey.Down,  true));
        t.Add(F("alt.left",  "CURSOR LEFT (ALT)",  "MISSION", ScopeMission, KeyboardKey.Left,  true));
        t.Add(F("alt.right", "CURSOR RIGHT (ALT)", "MISSION", ScopeMission, KeyboardKey.Right, true));
        // Menu-screen letters (intro shortcuts, heat dial, confirm). Internally unique and only
        // reachable while UpdatePlayer is NOT running, so they are NOT rebindable and NOT in
        // conflict with any mission verb — but a SYSTEM row (scope EVERYWHERE) that landed on one
        // of them WOULD double-fire on the intro, and this is what catches that.
        foreach (var (k, what) in new[]
        {
            (KeyboardKey.C, "CONTINUE"), (KeyboardKey.L, "LAST STAND"), (KeyboardKey.W, "WAR ROOM"),
            (KeyboardKey.K, "FIELD MANUAL"), (KeyboardKey.S, "SKIRMISH"), (KeyboardKey.Y, "DAILY"),
            (KeyboardKey.N, "TRAINING OP"), (KeyboardKey.U, "AUDIO CHECK"),
            (KeyboardKey.O, "CONTROLS"),
            (KeyboardKey.A, "HEAT -"), (KeyboardKey.D, "HEAT +"),
            (KeyboardKey.Left, "HEAT -"), (KeyboardKey.Right, "HEAT +"),
            (KeyboardKey.KpSubtract, "HEAT -"), (KeyboardKey.KpAdd, "HEAT +"),
            (KeyboardKey.Up, "MENU UP"), (KeyboardKey.Down, "MENU DOWN"),
            (KeyboardKey.Enter, "CONFIRM"), (KeyboardKey.Backspace, "EDIT TEXT"),
        })
            t.Add(F("menu." + k, what + " (MENUS)", "MENUS", ScopeMenu, k, true));

        return t.ToArray();
    }

    static readonly Dictionary<string, Bind> ById =
        Table.ToDictionary(b => b.Id, b => b, StringComparer.Ordinal);

    /// Every row, in screen order (fixed + hidden reservations included).
    public static IReadOnlyList<Bind> All => Table;
    /// The rows the CONTROLS screen draws (everything except the hidden reservations).
    public static IEnumerable<Bind> Rows => Table.Where(b => !b.Hidden);
    /// The rows the player may actually change.
    public static IEnumerable<Bind> Editable => Table.Where(b => !b.Fixed);

    public static Bind Get(string id) => ById.TryGetValue(id, out var b) ? b : null;

    // ---- reading ---------------------------------------------------------------------------
    /// Harness seam (SIGHTLINE_KEYLOG=1): print `KEYACT <id> <KEY>` whenever a bound action fires,
    /// so a live xdotool session can PROVE which action a physical key reached. Off by default; the
    /// flag is only ever set from Program.cs, so no shipped or measured path is affected.
    public static bool LogActions;

    /// True on the frame this action's key went down. The ONLY way gameplay reads a rebindable key.
    public static bool Pressed(string id)
    {
        var b = Get(id);
        if (b == null || b.Key == KeyboardKey.Null) return false;
        if (!Raylib.IsKeyPressed(b.Key)) return false;
        if (LogActions) Console.WriteLine("KEYACT " + id + " " + KeyName(b.Key));
        return true;
    }

    /// Held-down variant (no call site needs it yet; kept so a future hold-to-repeat control does
    /// not reintroduce a raw read).
    public static bool Down(string id)
    {
        var b = Get(id);
        return b != null && b.Key != KeyboardKey.Null && Raylib.IsKeyDown(b.Key);
    }

    public static KeyboardKey KeyOf(string id) => Get(id)?.Key ?? KeyboardKey.Null;

    /// The short display tag for an action's current key ("1", "TAB", "F11", "NONE").
    public static string Label(string id) => KeyLabel(KeyOf(id));

    // ---- key naming ---------------------------------------------------------------------------
    // Everything here is derived from Enum.GetName so the file never hardcodes a Raylib enum member
    // that might not exist. An entry that does not match simply falls through to its uppercased name.
    static readonly Dictionary<string, string> Pretty = new(StringComparer.Ordinal)
    {
        { "Zero", "0" }, { "One", "1" }, { "Two", "2" }, { "Three", "3" }, { "Four", "4" },
        { "Five", "5" }, { "Six", "6" }, { "Seven", "7" }, { "Eight", "8" }, { "Nine", "9" },
        { "Space", "SPACE" }, { "Enter", "ENT" }, { "Tab", "TAB" }, { "Backspace", "BKSP" },
        { "Escape", "ESC" }, { "Insert", "INS" }, { "Delete", "DEL" }, { "Home", "HOME" },
        { "End", "END" }, { "PageUp", "PGUP" }, { "PageDown", "PGDN" },
        { "Up", "UP" }, { "Down", "DOWN" }, { "Left", "LEFT" }, { "Right", "RIGHT" },
        { "Grave", "`" }, { "Minus", "-" }, { "Equal", "=" }, { "LeftBracket", "[" },
        { "RightBracket", "]" }, { "Backslash", "\\" }, { "Semicolon", ";" },
        { "Apostrophe", "'" }, { "Comma", "," }, { "Period", "." }, { "Slash", "/" },
        { "KpAdd", "KP+" }, { "KpSubtract", "KP-" }, { "KpMultiply", "KP*" },
        { "KpDivide", "KP/" }, { "KpDecimal", "KP." }, { "KpEnter", "KPENT" }, { "KpEqual", "KP=" },
    };

    /// The persisted spelling of a key (its enum name). Never localise or prettify this one.
    public static string KeyName(KeyboardKey k) => Enum.GetName(typeof(KeyboardKey), k) ?? "Null";

    /// The short on-screen tag for a key.
    public static string KeyLabel(KeyboardKey k)
    {
        if (k == KeyboardKey.Null) return "NONE";
        string n = KeyName(k);
        if (Pretty.TryGetValue(n, out string p)) return p;
        if (n.Length == 1) return n.ToUpperInvariant();                       // A..Z
        if (n.Length == 3 && n.StartsWith("Kp", StringComparison.Ordinal) && char.IsDigit(n[2]))
            return "KP" + n[2];                                               // Kp0..Kp9
        return n.ToUpperInvariant();                                          // F1..F12 and the rest
    }

    // ---- what may be bound ----------------------------------------------------------------------
    static HashSet<KeyboardKey> _assignable;

    /// Keys a player is allowed to bind. Derived from the enum by NAME so no member list is
    /// hardcoded: bare modifiers (they are not chords here and would read as stuck), the lock and
    /// system keys, and above all ESCAPE — the guaranteed route back to the pause menu — are out.
    public static bool IsAssignable(KeyboardKey k)
    {
        if (_assignable == null)
        {
            _assignable = new HashSet<KeyboardKey>();
            foreach (KeyboardKey v in Enum.GetValues(typeof(KeyboardKey)))
            {
                string n = Enum.GetName(typeof(KeyboardKey), v);
                if (string.IsNullOrEmpty(n)) continue;
                if (n == "Null" || n == "Escape") continue;                    // guarantee #1
                if (n.Contains("Shift") || n.Contains("Control") || n.Contains("Alt")
                    || n.Contains("Super")) continue;                          // bare modifiers
                if (n == "CapsLock" || n == "NumLock" || n == "ScrollLock"
                    || n == "PrintScreen" || n == "Pause") continue;           // locks / system
                if (n == "Menu" || n == "KeyboardMenu" || n == "Back") continue;
                if (n.StartsWith("Volume", StringComparison.Ordinal)) continue;
                _assignable.Add(v);
            }
        }
        return _assignable.Contains(k);
    }

    // ---- conflicts -------------------------------------------------------------------------------
    /// The row (other than `self`) that already claims `k` in a scope overlapping `scope`, or null.
    public static Bind Claimant(KeyboardKey k, int scope, Bind self)
    {
        if (k == KeyboardKey.Null) return null;
        foreach (var b in Table)
        {
            if (ReferenceEquals(b, self)) continue;
            if (b.Key != k) continue;
            if ((b.Scope & scope) != 0) return b;
        }
        return null;
    }

    /// Every live double-bind, as (a, b) pairs. Empty is the invariant; the CONTROLS screen draws
    /// whatever is left and `SelfCheck` fails on a non-empty result for the SHIPPED defaults.
    public static List<(Bind A, Bind B)> Conflicts()
    {
        var outp = new List<(Bind, Bind)>();
        for (int i = 0; i < Table.Length; i++)
            for (int j = i + 1; j < Table.Length; j++)
            {
                var a = Table[i]; var b = Table[j];
                if (a.Key == KeyboardKey.Null || a.Key != b.Key) continue;
                if ((a.Scope & b.Scope) == 0) continue;
                outp.Add((a, b));
            }
        return outp;
    }

    // ---- mutation ---------------------------------------------------------------------------------
    /// Rebind `id` to `k`. Returns null on success, else a short human-readable refusal. Refusals
    /// are the point: a silent double-bind is the bug this whole file exists to prevent.
    public static string Set(string id, KeyboardKey k)
    {
        var b = Get(id);
        if (b == null) return "unknown control";
        if (b.Fixed) return b.Label + " is reserved and cannot be rebound";
        if (!IsAssignable(k)) return KeyLabel(k) + " cannot be bound";
        if (b.Key == k) return null;                                   // no-op
        var other = Claimant(k, b.Scope, b);
        if (other != null)
            return KeyLabel(k) + " is already " + other.Label
                   + (other.Fixed ? " (reserved)" : "") + " - " + ScopeName(other.Scope);
        b.Key = k;
        Display.SaveKeymap();
        return null;
    }

    /// Restore every row to its shipped default. Always reachable from the CONTROLS screen, which is
    /// always reachable from the pause menu, which is always reachable with Escape.
    public static void ResetAll()
    {
        foreach (var b in Table) b.Key = b.Def;
        Display.SaveKeymap();
    }

    public static bool IsDefault(Bind b) => b.Key == b.Def;
    public static bool AnyChanged() => Table.Any(b => b.Key != b.Def);

    // ---- persistence ------------------------------------------------------------------------------
    /// Only the overrides, as `id=KeyName` joined by `;`. Empty string when nothing was changed, so a
    /// default profile writes nothing and a later change to a shipped default reaches that player.
    public static string Encode()
    {
        var sb = new StringBuilder();
        foreach (var b in Table)
        {
            if (b.Fixed || b.Key == b.Def) continue;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(b.Id).Append('=').Append(KeyName(b.Key));
        }
        return sb.ToString();
    }

    /// Rebuild the map from an encoded string. NEVER trusts it: the file may have been hand-edited,
    /// written by a newer build, or corrupted. Everything resets to defaults first, the overridden
    /// rows are UNBOUND, and only then are the overrides applied one at a time through the same
    /// conflict check the UI uses. Unbinding first is what lets an honest SWAP survive a reload
    /// (HUNKER->H and HACK->3 would each look like a conflict against the other's default).
    /// Anything that still does not fit is dropped and reported. Returns the count dropped.
    public static int Decode(string s)
    {
        foreach (var b in Table) b.Key = b.Def;
        if (string.IsNullOrWhiteSpace(s)) return 0;

        var wanted = new List<(Bind b, KeyboardKey k)>();
        int dropped = 0;
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) { dropped++; continue; }
            var bind = Get(part.Substring(0, eq).Trim());
            if (bind == null || bind.Fixed) { dropped++; continue; }
            if (!Enum.TryParse(part.Substring(eq + 1).Trim(), false, out KeyboardKey k)
                || !IsAssignable(k)) { dropped++; continue; }
            if (wanted.Any(w => ReferenceEquals(w.b, bind))) { dropped++; continue; }   // duplicate id
            wanted.Add((bind, k));
        }
        foreach (var (b, _) in wanted) b.Key = KeyboardKey.Null;   // clear first, so swaps survive
        foreach (var (b, k) in wanted)
        {
            if (Claimant(k, b.Scope, b) != null) { dropped++; continue; }
            b.Key = k;
        }
        // A row the file left unbound is not a strand (Escape is fixed and RESET DEFAULTS is always
        // reachable) but it IS a surprise, so put the default back unless the default is now taken.
        foreach (var (b, _) in wanted)
            if (b.Key == KeyboardKey.Null && Claimant(b.Def, b.Scope, b) == null) b.Key = b.Def;
        return dropped;
    }

    // ---- integrity -----------------------------------------------------------------------------
    /// The structural invariant, asserted by SIGHTLINE_KEYTEST: the SHIPPED table double-binds
    /// nothing. This is the check that would have failed on day one of the `F` bug.
    public static string SelfCheck()
    {
        var c = Conflicts();
        if (c.Count == 0) return null;
        return string.Join(", ", c.Select(p => KeyLabel(p.A.Key) + " = " + p.A.Label + " AND " + p.B.Label));
    }

    // ---- self-test (SIGHTLINE_KEYTEST=1) --------------------------------------------------------
    /// Window-free. Asserts, in order: the shipped defaults are conflict-free; every id is unique
    /// and every default is legal; a rebind round-trips through the REAL encode/decode; a conflict
    /// is refused with a message naming the holder; a cross-scope reuse is ALLOWED (no manufactured
    /// conflicts); a swap survives a save/load; a hand-edited/garbage map is repaired rather than
    /// shipped double-bound; and — the one that matters — NOTHING the player or a corrupt file can
    /// do takes Escape away from the pause menu.
    public static string SelfTest()
    {
        var fails = new List<string>();
        string saved = Encode();          // restore the caller's map at the end
        try
        {
            void Chk(bool ok, string what) { if (!ok) fails.Add(what); }

            // 1. the shipped table is internally consistent
            ResetInternal();
            string sc = SelfCheck();
            Chk(sc == null, "shipped defaults double-bind: " + sc);
            Chk(Table.Select(b => b.Id).Distinct().Count() == Table.Length, "duplicate action id in the table");
            foreach (var b in Table)
            {
                if (b.Fixed) continue;
                Chk(IsAssignable(b.Def), "default for " + b.Id + " (" + KeyLabel(b.Def) + ") is not assignable");
                Chk(!string.IsNullOrEmpty(b.Label), "no label for " + b.Id);
                Chk(b.Scope != 0, "no scope for " + b.Id);
            }
            // sensitivity probe: the conflict scan must actually be capable of failing
            var probe = Get(Shoot); var probeKey = probe.Key;
            probe.Key = KeyOf(Overwatch);
            Chk(SelfCheck() != null, "conflict scan cannot detect a planted double-bind");
            probe.Key = probeKey;

            // 2. ESCAPE IS UNTOUCHABLE (guarantee #1)
            Chk(!IsAssignable(KeyboardKey.Escape), "Escape is assignable - a rebind could strand the player");
            Chk(Get("menu").Fixed && Get("menu").Key == KeyboardKey.Escape, "the CANCEL/MENU row is not fixed on Escape");
            foreach (var b in Editable)
                Chk(Set(b.Id, KeyboardKey.Escape) != null, "Set(" + b.Id + ", Escape) was ACCEPTED");
            Chk(Get("menu").Key == KeyboardKey.Escape, "Escape moved off the menu row");
            // and no encoded map, however hostile, can move it
            Decode("menu=Q;menu=Null;alt.up=Escape;shoot=Escape");
            Chk(Get("menu").Key == KeyboardKey.Escape, "a hand-edited map moved CANCEL/MENU off Escape");
            Chk(KeyOf(Shoot) != KeyboardKey.Escape, "a hand-edited map bound FIRE to Escape");
            ResetInternal();

            // 3. a rebind round-trips through the real encode/decode
            Chk(SetQuiet(Shoot, KeyboardKey.Q) == null, "rebind FIRE -> Q refused");
            Chk(KeyOf(Shoot) == KeyboardKey.Q, "FIRE did not take Q");
            Chk(Label(Shoot) == "Q", "FIRE label is " + Label(Shoot) + ", expected Q");
            string enc = Encode();
            Chk(enc.Contains("shoot=Q"), "encode lost the override: '" + enc + "'");
            Chk(!enc.Contains("overwatch"), "encode wrote an untouched row: '" + enc + "'");
            ResetInternal();
            Chk(KeyOf(Shoot) == KeyboardKey.One, "reset did not restore FIRE");
            Chk(Decode(enc) == 0, "decode dropped a valid override");
            Chk(KeyOf(Shoot) == KeyboardKey.Q, "FIRE did not survive the round-trip");
            Chk(SelfCheck() == null, "the round-tripped map double-binds");
            ResetInternal();

            // 4. conflicts are REFUSED and the refusal names the holder (guarantee #2)
            string err = SetQuiet(Shoot, KeyOf(Overwatch));
            Chk(err != null, "binding FIRE onto OVERWATCH's key was accepted");
            Chk(err != null && err.Contains("OVERWATCH"), "the refusal does not name OVERWATCH: " + err);
            Chk(KeyOf(Shoot) == KeyboardKey.One, "a refused rebind still moved the key");
            // the historical bug, reproduced: a SYSTEM row landing on a MISSION verb's key
            err = SetQuiet(Fullscreen, KeyOf(FocusOw));
            Chk(err != null, "FULLSCREEN was allowed onto FOCUS's key - the W6 `F` bug is back");
            // and onto a MENU-screen shortcut, which a mission verb may legally use
            err = SetQuiet(Mute, KeyboardKey.L);
            Chk(err != null, "MUTE (everywhere) was allowed onto the intro's LAST STAND key");

            // 5. scopes are REAL: a pause-menu letter may be reused by a mission verb
            Chk(SetQuiet(Hunker, KeyboardKey.K) == null,
                "a MISSION verb was refused the pause menu's K - false conflict across scopes");
            Chk(SelfCheck() == null, "the cross-scope reuse was reported as a live conflict");
            ResetInternal();

            // 6. a SWAP survives a save/load (the reason Decode unbinds before it assigns)
            var hK = KeyOf(Hunker); var kK = KeyOf(Hack);
            Get(Hunker).Key = KeyboardKey.Null;
            Chk(SetQuiet(Hack, hK) == null, "swap step 1 refused");
            Chk(SetQuiet(Hunker, kK) == null, "swap step 2 refused");
            string swap = Encode();
            int lost = Decode(swap);
            Chk(lost == 0, "the swap lost " + lost + " binding(s) on reload");
            Chk(KeyOf(Hack) == hK && KeyOf(Hunker) == kK, "the swap did not survive the round-trip");
            Chk(SelfCheck() == null, "the reloaded swap double-binds");
            ResetInternal();

            // 7. a hostile / corrupt map is repaired, never shipped double-bound
            foreach (string bad in new[]
            {
                "shoot=One;overwatch=One",            // an explicit double-bind
                "shoot=NotAKey;overwatch=Two",        // an unparseable key
                "shoot",                              // no '='
                "=One",                               // no id
                "nosuchaction=Q",                     // an id from a newer build
                "shoot=LeftShift",                    // a non-assignable key
                "shoot=Q;shoot=Z",                    // the same id twice
                ";;;;",                               // noise
                new string('x', 4000),                // a large garbage blob
            })
            {
                Decode(bad);
                Chk(SelfCheck() == null, "a corrupt map survived Decode double-bound: '" + Trunc(bad) + "'");
                Chk(Get("menu").Key == KeyboardKey.Escape, "a corrupt map cost the player Escape: '" + Trunc(bad) + "'");
            }
            ResetInternal();

            // 8. THE STRAND TEST. Bind every editable row to something exotic, reload it, and prove
            //    the pause menu is still reachable and RESET DEFAULTS still restores play.
            var pool = new[]
            {
                KeyboardKey.Q, KeyboardKey.Z, KeyboardKey.J, KeyboardKey.O, KeyboardKey.I,
                KeyboardKey.F1, KeyboardKey.F3, KeyboardKey.F4, KeyboardKey.F5, KeyboardKey.F6,
                KeyboardKey.F7, KeyboardKey.F8, KeyboardKey.F9, KeyboardKey.F10, KeyboardKey.F12,
                KeyboardKey.Insert, KeyboardKey.Delete, KeyboardKey.Home, KeyboardKey.End,
                KeyboardKey.PageUp, KeyboardKey.PageDown, KeyboardKey.Minus, KeyboardKey.Equal,
                KeyboardKey.LeftBracket, KeyboardKey.RightBracket, KeyboardKey.Semicolon,
                KeyboardKey.Apostrophe, KeyboardKey.Comma, KeyboardKey.Period, KeyboardKey.Slash,
                KeyboardKey.Backslash, KeyboardKey.Grave,
            };
            foreach (var b in Editable.ToList()) b.Key = KeyboardKey.Null;
            int pi = 0;
            foreach (var b in Editable.ToList())
                if (pi < pool.Length) { b.Key = pool[pi]; pi++; }
            string wrecked = Encode();
            Decode(wrecked);
            Chk(Get("menu").Key == KeyboardKey.Escape, "the worst legal remap took Escape away");
            Chk(SelfCheck() == null, "the worst legal remap double-binds");
            ResetAllQuiet();
            Chk(!AnyChanged(), "RESET DEFAULTS did not restore every row");
            Chk(KeyOf(Shoot) == KeyboardKey.One && KeyOf(EndTurn) == KeyboardKey.Enter,
                "RESET DEFAULTS left the map wrong");

            // 9. labels are drawable (no empty tag on the action bar / pause card)
            foreach (var b in Table)
                Chk(!string.IsNullOrEmpty(KeyLabel(b.Key)), "empty key label for " + b.Id);
        }
        catch (Exception ex) { fails.Add("threw: " + ex.Message); }
        finally { ResetInternal(); Decode(saved); }

        if (fails.Count == 0)
            return "KEYTEST: PASS\n  " + Table.Length + " rows (" + Editable.Count() + " rebindable, "
                 + Table.Count(b => b.Fixed) + " reserved) - defaults conflict-free, Escape unreachable by any remap";
        return "KEYTEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    static string Trunc(string s) => s.Length <= 40 ? s : s.Substring(0, 40) + "...";
    /// Reset without touching disk (the self-test must never write the player's settings).
    static void ResetInternal() { foreach (var b in Table) b.Key = b.Def; }
    static void ResetAllQuiet() => ResetInternal();
    /// Set without touching disk — same validation path as Set, used by the self-test.
    static string SetQuiet(string id, KeyboardKey k)
    {
        var b = Get(id);
        if (b == null) return "unknown control";
        if (b.Fixed) return "reserved";
        if (!IsAssignable(k)) return KeyLabel(k) + " cannot be bound";
        var other = Claimant(k, b.Scope, b);
        if (other != null) return KeyLabel(k) + " is already " + other.Label;
        b.Key = k;
        return null;
    }
}
