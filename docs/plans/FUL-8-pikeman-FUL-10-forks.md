# FUL-8 PIKEMAN + FUL-10 FORKS — re-derived dev-executable specs (2026-08-28, vs branch tip e4842da)

Derived against the CURRENT tree (FUL-1/2/3/4/11/12 landed). Every seam below was verified to exist
at the cited file:line. Style matches the ROADMAP FUL-N entries; both are ready to paste into
docs/ROADMAP.md in place of the stub lines.

---

## FUL-8 PIKEMAN (P8, M)

**Goal.** The SARISSA ("PIKEMAN") — a Wardens lane-holder specialist that visibly BRACES a movement
lane (a foe-red focus cone on the board) and STAGGERS the first soldier through. The
21-archetype roster contests HP (rushers), information (SPOTTER/SCREENER), morale (WARBRINGER) and
progress (CUSTODIAN) — nothing contests WHERE YOU MAY WALK. This is that piece, and it teaches the
player's own BRACE [B] by mirroring it exactly.

**Keystone finding — zero new combat machinery; the BRACE plumbing is already team-symmetric
(verified in code):** `Game.OnUnitEnteredTile` (src/Game.cs:2059-2107) picks watchers as
`mover.Team == Player ? Enemies : Players` — an ENEMY with `OnOverwatch` already reacts to a player
mover through the identical path. `w.OwBrace` (Game.cs:2094-2095) halves damage + no-crit
(`Combat.BraceFullDamage` at src/Combat.cs:579 is Team.Player-gated, so an enemy brace ALWAYS takes
the halving — COMBATTEST already pins `shockDoctrineEnemyExcluded`, Combat.cs:1577) and sets
`ShotAnim.Stagger`; `ShotAnim.Apply` (src/Anim.cs:319-326) zeroes the mover's `ActionsLeft` and
drops its held watch. The cone gate `w.OwFocused && !InOwCone(w, mover.X, mover.Y)` (Game.cs:2073)
and the one-reaction cap `ReactedThisTurn` (Game.cs:2070) are team-agnostic too. Reaction aim for an
enemy = -10 + `Combat.FocusOwAim` (15, Combat.cs:75) = net +5 (no Reflexes/Guardian on enemies) —
vs a 58-aim archetype that's a ~55-65% stagger chance on an exposed mover: a threat, not a script.
The feature is therefore: an Ai branch that ARMS the flags + a renderer read + a bot read + a codex
row + a harness leg.

**Archetype (Mission.cs; stats in the W8 Wardens-support band — WARBRINGER 8/56/5, CUSTODIAN 5/48/6):**
`MakeHostile("SARISSA", "PIKEMAN", WeaponKind.Smg, 7 + bump, 58 + bump, 5, x, y)`
- SMG (MaxRange 10, Unit.cs:118) keeps the braced cone LOCAL and readable — the cone is the pike,
  the gun is flavour. HP 7: survives one focused soldier-turn, dies to two (the counter costs real
  actions). Mob 5: a holder, not a rusher.
- Callsign "SARISSA" verified free of collisions: the soldier `Callsigns` pool CONTAINS "PIKE"
  (Mission.cs:792) — do NOT reuse it; also absent from Nicknames.Pool and the bestiary.
- NO enum is touched anywhere in this wave (Cls is a string; see save-compat).

**Ai.cs — the plant (a dedicated archetype branch on the MEDIC/CUSTODIAN/BOMBARD pattern; insert
after the CUSTODIAN branch, gated `e.Cls == "PIKEMAN" && e.Routed == 0` per the W8 routed-specialist
rule, Ai.cs:114-118):**
1. Lane anchor = nearest non-VIP soldier.
2. Opportunism first (identity: holder, not statue — mirrors BOMBARD's fall-through when nothing is
   worth shelling, Ai.cs:227): if it already has a >=65% shot on an EXPOSED soldier, take the shot
   via the generic loop instead of planting.
3. Else if any soldier is within cone reach (dist <= Weapon.MaxRange + 2): pick a plant tile from
   `reach` with `acts <= 1` (keep the action to plant), scored SPOTTER-style (Ai.cs:499-511):
   cover*16 + height*8 − |distNearest − 4|*1.4 − 22 if distNearest <= 2 + jitter.
4. Emit new `EnemyPlan` fields: `public bool Brace; public int BraceDirX, BraceDirY;`
   (dir = anchor − plant tile). Nothing in reach / boxed out → fall through to the generic combat
   loop — never a dead turn / no TIMEOUT (the MORTAR safety, Ai.cs:225-228).

**Game.cs — exec (new else-if in the ActAfterMove chain, BEFORE the ShootTarget branch; the
SiegeCharge branch at Game.cs:5292-5305 is the template):**
`e.OnOverwatch = true; e.OwBrace = true; e.OwFocused = true; e.OwDirX/OwDirY = plan dir;
e.ActionsLeft = 0; Fx.PopText("BRACED", Pal.Foe); Audio.Play("over");`
The plant lives exactly one round: the enemy-side BeginTurn wipes OnOverwatch/OwBrace/OwFocused
(Unit.cs:705-707), so holding the lane costs the PIKEMAN its action EVERY turn — the
movement-economy trade is symmetric with the player's own BRACE.

**What breaks the plant (all existing seams, no new code — this list IS the codex counterplay):**
kill it; STAGGER it back — a player BRACE reaction (ShotAnim.Apply drops the victim's watch,
Anim.cs:322 — the teach-by-mirror beat) or the BREACHER grapple-stagger (Game.cs:3922-3929); FLASH
it (FlashAnim strips OnOverwatch, Anim.cs:732; Disoriented also blocks the re-plant — gate the Ai
branch on `!e.HasStatus(Disoriented)`, mirroring the enemy-overwatch gate at Game.cs:5392); SHOVE it
(ShoveAnim strips the target's watch, Anim.cs:85); rout its pod (`Routed > 0` skips the branch;
BreakPodMorale, Game.cs:2279); smoke/LoS-break the lane (the reaction runs through CanTarget); walk
OUTSIDE the 90-degree cone (blind); or feed it ONE cheap step — `ReactedThisTurn` caps it at one
stagger per round (Game.cs:2070), so a sacrificial half-move opens the lane for the squad.

**Board read (Renderer.cs — reuse the existing focus-cone/brace vocabulary; verify both palettes):**
- `DrawOverwatchThreat` (Renderer.cs:873-927) gains the truth gate
  `if (w.OwFocused && !g.InOwCone(w, x, y)) continue;` in its tile loop — zero-regression today (no
  enemy sets OwFocused) and it makes the red wash exactly mirror the reaction gate.
- Factor DrawFocusCones' cone-edge rays + direction chevron block (Renderer.cs:846-869) into a
  shared helper; draw it for any braced+focused ENEMY watcher in Pal.Foe over the wash. The "BRC"
  overhead tag is already team-agnostic (Renderer.cs:2247-2249) — a planted PIKEMAN gets it free.
- New "PIKEMAN" case in the DrawUnit silhouette switch (Renderer.cs ~1646-1714, STRIKER/SCREENER/
  CUSTODIAN precedents) + it flows into `DrawCodexGlyph` (Renderer.cs:1715): squat braced body + a
  long diagonal pike line with a crossbar — at squint: "a line pointing down a lane".
- Honesty fix riding along: the STAGGERED pop in ShotAnim.Apply (Anim.cs:323) is Pal.Good
  unconditionally — correct for the player's comeback, a lie when YOUR soldier is staggered. Color
  by victim team (Pal.Foe when D.Team == Player).

**Teaching (the mirror):** Codex bestiary row (Codex.cs:63 table) —
`("SARISSA", "PIKEMAN", "Lane-holder — plants a braced cone over a movement lane and STAGGERS the
first soldier through: reduced damage, but your action is denied. It is exactly your own BRACE.
Break its watch, go around the cone, or feed it a cheap step first.")` — plus "PIKEMAN" in the
CODEXTEST `required` array (Codex.cs:396-401). NEW CONTACT banner + dormant/live enemy-ID hovers are
automatic off the row (Codex.NameFor/BlurbFor/BlurbClause/TipFor, Codex.cs:106-137; Game's
_seenArchetypes/CheckNewContact needs nothing). Optional ride-along: first PIKEMAN sighting
force-shows the one-shot BRACE field tip if unseen (`UpdateBraceCallout` force path, Game.cs:601-617).

**Spawn weights + gating (Mission.cs — the W8 WARBRINGER/CUSTODIAN precedent: a faction-native
window + a small cascade tail, mission-TIER gated, failed gates route to filler so any roll
resolves; exactly one `r` per spawn, only the windows move — CRN draw-count neutral):**
- Wardens `FactionRoster` (Mission.cs:751-781): **10% at m2+** — the teaching piece arrives early,
  like Legion's m2 STRIKER/LANCER. Re-slice: SNIPER 24→20 (r<0.20), MORTAR 18 (r<0.38), SIEGE 12
  m3+ (r<0.50, m2 routes SCOUT), SCREENER 10 m3+ (r<0.60, m2 routes SCOUT), **PIKEMAN 10 m2+
  (r<0.70, m1 routes SCOUT)**, MEDIC 14→12 (r<0.82), CUSTODIAN 8→6 m3+ (r<0.88, else GRUNT-route),
  GRUNT 6 (r<0.94), SCOUT filler.
- Default cascade m3+ (Mission.cs:624-688): a **~3% mid-tail slot** (carve between SCREENER and
  BOMBARD; keep every archetype's first-appearance tier and the 1% GRUNT/SCOUT/BRUISER tails).
- Defend rich waves + LAST STAND inherit it free (MakeWaveHostile→MakeEndlessHostile→
  SelectArchetype, Mission.cs:991-1023); NO demote needed — unlike the BOMBARD its telegraph is
  drawn where it stands, and a wave PIKEMAN plants in plain sight. No heat gate in v1; if FUL-13
  wants one, the W6 `Ai.Tier >= 1` finale-body gate (Mission.cs:437) is the pattern.

**Autopilot awareness (minimum viable per the plan: the lane is danger tiles):** new Game helper
`InEnemyBraceLane(int x, int y)` mirroring `InSiegeZone` (Game.cs:2499-2503): any alive+Active enemy
with OnOverwatch+OwBrace, ammo > 0, in range + LoS + InOwCone of (x,y). Then one line in
`TileExposure` (Game.Autopilot.cs:1194-1209): `if (InEnemyBraceLane(x,y)) threat += 18f;` — between
an exposed gun (~6-10) and the siege zone's 30 (a stagger costs a turn, not a life). That routes
SmartApproach/ScoreDestTile/SmartStep around the lane with no other bot change.

**Save-compat: NONE touched — verified.** Enemies are never serialized (SaveGame persists
Run/Squad/RunDto only; Unit's enemy-side state is documented "transient — enemies aren't saved",
e.g. Unit.cs:496-541). Cls is a string; the weapon reuses WeaponKind.Smg; no Perk/Boon/Trait/
WeaponMod/Objective/Faction member is appended. SAVETEST unchanged.

**Harness leg — `SIGHTLINE_PIKETEST=1` → `Game.PikemanSelfTest()`** (Game.Harness.cs; register in
Program.cs on the STAGGERTEST pattern, Program.cs:181-185). Controlled scene via the DKTEST
fixed-damage MkWatcher pattern (Game.Harness.cs:1326-1346):
- (a) PLANT: pikeman + soldier at dist 5 → `Ai.Plan` returns `Brace == true`, dir toward the
  soldier; exec arms OnOverwatch+OwBrace+OwFocused with OwDir set.
- (b) PLANT→STAGGER (the plant->stagger->break sequence + the numeric pin): pikeman Aim 95,
  `DmgMin=DmgMax=4, CritBase=0`; drive the mover in-cone via `OnUnitEnteredTile` → the queued
  reaction `ShotAnim` has `Stagger == true`; bounded re-stage loop until the reaction connects (the
  DKTEST 60-attempt pattern); after Apply: mover Alive, `ActionsLeft == 0`, damage taken **== 2**
  (the `Math.Max(1, 4/2)` halving pin — COMBATTEST-style numeric), `Res.Crit == false`.
- (c) CONE BLINDNESS: a mover behind the pikeman provokes NO reaction (STAGGERTEST leg-1 negative).
- (d) BREAK: a player braced reaction that hits the pikeman drops its plant (OnOverwatch false via
  Anim.cs:322); a Disoriented pikeman's Plan emits no Brace.
- Shot hook `SIGHTLINE_PIKESHOT=1`: stage a planted lane vs two soldiers (the WAVEBANNER/BRACETIP
  staging pattern) for the cone wash + rays + BRC tag + ID hover.

**Verify.** Release 0/0; PIKETEST + STAGGERTEST + COMBATTEST + AITEST + SAVETEST + CODEXTEST PASS;
autoplay x5 no-exception/no-TIMEOUT; `SIGHTLINE_BALANCE=20` at h0 (+ one h4 leg): PIKEMAN in the
composition table (Stats.RecordSpawn is automatic — expect ~2-4% default / ~10% Wardens),
completion delta within ±5 of reference, Escort/Evac mean turns within +1t (lane denial must tax
routes, not stall them). Screenshots: PIKESHOT in BOTH palettes (SIGHTLINE_CB=1 — new coded state
rule, DESIGN.md §3.H) + one live Wardens m3 board shot.

**Effort:** M. **Files:** src/Mission.cs, src/Ai.cs, src/Game.cs, src/Game.Autopilot.cs,
src/Renderer.cs, src/Anim.cs, src/Codex.cs, src/Game.Harness.cs, src/Program.cs
(+ docs/ROADMAP.md, docs/DEVLOG.md, docs/FEATURES.md).

---

## FUL-10 FORKS (P10, M)

**Goal.** 6-8 new trade-off field events wired into the salvage / scar / veteran / faction systems;
two draft contracts that engage the W9 veteran economy; the orphaned perks reachable. Consumes
FUL-1's BY EVENT-CHOICE table.

**Verified findings the spec builds on:**
- **The three orphaned perks are `Perk.Vantage`, `Perk.Breaker`, `Perk.Siegebreaker`** (the HORIZON
  W6 trio — the goal line's "COUNTERPLAY" attribution is stale; the count of three matches). They
  ARE in `PerkDef.All` (Unit.cs:736-745) so the random slot-B can roll them, but they appear in NO
  `Run.ClassLine` (Run.cs:1290-1308) — they never get the class-biased slot A of `MakePerkOffer`
  (Run.cs:1315-1340), and the table's own doc "Every perk appears in >=1 line" (Run.cs:1288) is
  false today.
- Event ids are stable strings (`GameEvent.Id`, Events.cs:58); FUL-1's `Stats.RecordEvent(id, arm)`
  (Stats.cs:173-177, called at Game.cs:5984) keys `"id:arm"` where arm = choice INDEX → **arms are
  append-only within an event, ids never rename**.
- `EventOutcomeKind` is NEVER persisted (outcomes apply immediately; only the mutated Run/Unit
  state saves — EVENTTEST §4 round-trips the RESULT, Events.cs:495-531). Extending it is save-free;
  append at the end by house style anyway.
- Event→node selection is `hash(MapSeed, node) % All.Length` + linear-probe dedupe
  (`IndexForNode`, Events.cs:194-212): growing `All` changes which event an IN-FLIGHT save's
  unvisited "?" node shows after upgrading. Not corruption (resolved events are baked into Run
  state; MapPos/Visited semantics hold) — an accepted, harmless version skew; note it in the DEVLOG.
- `Contract` is persisted by ordinal (`RunDto.Contract`, SaveGame.cs:511) → append-only, and TWO
  self-test TAIL PINS must move with the append: SaveGame.cs:675 (`contractVals[^1] !=
  Contract.Spearhead`) and CONTRACTTEST (Game.Harness.cs:1898-1901: `All.Length != 3` + `cv[^1]`).
- Salvage is META (meta.json bank) and `EventCatalog.Apply` must stay pure/headless (EVENTTEST calls
  it directly) → **an event may never call SaveGame.AddSalvage**. Route through a new persisted Run
  field committed at run end by `AwardMetaRunEnd` (Game.cs:1863-1905) — quit-safe by construction
  (the W9 pending-ledger lesson, inverted for income).
- Faction hooks that already run end-to-end: `Run.PrepFaction` (one-mission counter-prep, persisted,
  consumed at SetupMission → Combat.BeginMission, Game.cs:1229-1230) + `Run.UpcomingFaction()`
  (Run.cs:433-438); `Scar.Vendetta` + `Unit.VendettaFaction` (persisted, read in Combat.ComputeOdds).
- The bot's event policy is safe-first (`AutoEventChoice`/`IsSafeChoice`/`HasDownside`,
  Game.cs:5956-5976) — **HasDownside must learn the new downside kinds** or the bot will pick a
  scarring arm as "safe"; honest arm-uptake measurement then waits on FUL-5's hashed 70/30
  value-biased chooser (deliberate sequencing: FUL-10 makes the forks exist + measurable, FUL-5
  makes the bot walk them).

**New outcome kinds (append to `EventOutcomeKind`, Events.cs:18-33; each gets an `Apply` case
(pure), an EVENTTEST mutation leg, a `ChoiceLegal` read where needed (Game.cs:5940), and a
`HasDownside` entry where it hurts):**
- `GrantScar` — add Scar `Sc` to a soldier (reuse the `Tr` slot pattern: new `Scar Sc` field on
  EventOutcome); when `Sc == Vendetta` also set `VendettaFaction = run.UpcomingFaction()` (fallback
  Legion). `ChancePct > 0` = seeded risk via the existing `GambleSucceeds(run, node, pct)`
  (Events.cs:219-225) so it's reload-stable. Downside kind.
- `CureScar` — remove the target's newest scar, reverting BurnScarred's +MaxHp grant (mirror the
  barracks REHAB revert, Game.Meta.cs:148-155). Legal only if someone is scarred.
- `Salvage` — `run.PendingSalvageReward += Amount` (new field, below). Meta income, run-committed.
- `GrantPrep` — `run.PrepFaction = run.UpcomingFaction()`; None-telegraphed degrades to a report
  line ("no faction telegraphed"). First non-shop entry into the counter-prep system.
- `RankKills` — `u.Kills += Amount` (lowest-rank soldier); the barracks pass's existing
  PromoteEligible ranks it (the depth-scaled MakeRecruit precedent, Mission.cs:804-828). Engages
  the W9 economy forward: a higher rank = a higher 10+8xRank recall price NEXT run.
- (roster release for "reservecall") `ReleaseSoldier` — remove the highest-rank soldier
  (guard `Squad.Count > 1` in ChoiceLegal, the Recruit-gate precedent at Game.cs:5948).

**Plumbing:** `Run.PendingSalvageReward` int (default 0) + `RunDto.PendingSalvageReward`
(append-only field, SaveGame.cs:497-512) + a SAVETEST round-trip pin; `AwardMetaRunEnd` adds it to
the bounty on win OR loss (it was earned) and folds it into `EndSalvage` so the FUL-12 end-card
slab shows it (Game.cs:1873-1874).

**The seven events (id / title / arms in index order — every event crosses >=2 DIFFERENT resources;
every event has a genuinely declinable arm; ids + arm order frozen forever for the compass):**
1. `warpension` OLD DEBTS — a discharged reserve veteran calls in a debt.
   0 "Pay it" (-15 intel → Salvage +25 at run end: run-currency now vs meta-currency later);
   1 "Press them back into service" (RankKills +2 on your greenest soldier + GrantScar ShellShocked
   on the same soldier — a harder veteran, marked by it);
   2 "Refuse" (Nothing).
2. `fieldhospital` FIELD HOSPITAL — a grey-market surgeon offers real work on old wounds.
   0 "Buy the surgery" (-20 intel → CureScar — the 30-SALVAGE barracks REHAB's intel-priced mirror:
   cross-currency arbitrage IS the decision);
   1 "Volunteer for trials" (HealSoldier full + GrantScar HardBitten at ChancePct 40, seeded);
   2 "Walk away" (Nothing).
3. `informant` FACTION INFORMANT — a deserter sells the next force's doctrine.
   0 "Buy the dossier" (-12 intel → GrantPrep: counter-prep staged without the shop slot);
   1 "Turn them in" (+20 intel + AddHeat 1 — the faction tightens up);
   2 "Let them go" (Nothing).
4. `quartermaster` CROOKED QUARTERMASTER — the requisition ledgers can be cooked, once.
   0 "Cook them" (Salvage +20 at run end + WoundSoldier 1 — someone takes the fall);
   1 "Report the racket" (+14 intel);
   2 "Skim the crates" (GrantGrenades 2).
5. `bloodfeud` BLOOD FEUD — a soldier recognizes the outfit that nearly took them.
   0 "Swear the feud" (GrantScar Vendetta, VendettaFaction = upcoming faction — the +10 aim/+8 crit
   grudge WITH its scar cost, riding the existing Combat.ComputeOdds read);
   1 "Counsel restraint" (HealSoldier +3);
   2 "Channel it" (GrantBonusPerk + WoundSoldier 1 — the drill precedent).
6. `reservecall` THE RESERVE CALLS — HQ asks you to release a proven soldier to another cell.
   0 "Release them" (ReleaseSoldier highest-rank + Salvage +30 at run end + Intel +10 — trade a
   veteran SLOT for the economy);
   1 "Keep the roster" (AddHeat 1 — HQ notes the refusal).
7. `warchest` SEALED WAR CHEST — a faction pay-chest, booby-trapped and singing.
   0 "Force it" (seeded gamble: ChancePct 55 → Salvage +35 at run end, else WoundSoldier 1);
   1 "Sell the location" (+18 intel);
   2 "Leave it" (Nothing).
Coverage: salvage 1/4/6/7 · scars 1/2/5 · veteran-rank/slot 1/6 · faction prep/vendetta/heat 3/5/6
· wounds/heal 2/4/5/7 · intel everywhere. Ten existing + seven new = 17 catalog entries over the
1-2 "?" nodes/run (GenerateMap clamp, Run.cs:511) ≈ each event ~1-in-9 runs — do NOT widen the
stamp clamp in this wave (GenerateMap is re-run from MapSeed on load, so changing it silently
reshapes IN-FLIGHT saves' unvisited nodes); park a `Clamp(mids/4, 1, 3)` exposure lever for FUL-13
with that caveat attached.

**The two contracts (append after `Contract.Spearhead`; ContractDef.All/Name/Code/Desc/Flavor/Parse
rows, Run.cs:61-116; Hud draft cards `conCards` +2 with a layout re-fit, Hud.cs:3200; the Codex
CONTRACTS tab auto-iterates, Codex.cs:375):**
- `Contract.MercenaryClause` "MERCENARY CLAUSE" (MRC): **veteran recalls cost HALF (round up) at
  the draft, but this run's survivors are never enshrined into the reserve.** Seams:
  `Game.DraftRecallCost` (Game.cs:859-866) reads `DraftSelectedContract` (both known pre-run at the
  draft screen; the Hud bills at Hud.cs:3049/3236 read the same property, so the discount is
  automatically honest); skip `SaveGame.EnshrineVeterans` (Game.cs:1893-1894) when active. Cheap
  veteran power now vs a stalled reserve pipeline — a real fork against W9's 10+8xRank table
  (MetaProg.RecallCost, src/Meta.cs:76-77).
- `Contract.LivingLegends` "LIVING LEGENDS" (LGD): **surviving Rank>=2 soldiers each pay a run-end
  PENSION (+6xRank salvage, folded into AwardMetaRunEnd/EndSalvage) and soldiers count kills DOUBLE
  toward rank (the IronVeterans `u.Kills += 1` site, Run.cs:1116) — but any KIA whose name matches
  a reserve record ERASES that record** (new `SaveGame.RemoveVeterans(names)` applied to
  `Run.Fallen` ∩ reserve inside AwardMetaRunEnd; name-keyed exactly like EnshrineVeterans' dedupe,
  SaveGame.cs:428). Veterans become mortal ACROSS runs — the W9 economy gets stakes, not just
  prices. Both stay `Contract == X`-gated and inert at None (the headless/base-balance invariant,
  Run.cs:55-59).

**Orphaned-perk fix (Run.ClassLine, Run.cs:1290-1308 — slot-A reachability, and the doc comment
made true again):**
- `Vantage` → SHARPSHOOTER (the perch class: Sniper RangeMod + high-ground identity) + GUNNER
  (BIPOD/planted overlap);
- `Breaker` → ASSAULT (the class that closes to punish the gunner's pin — the pin ends the gunner's
  own turn, so the FOLLOW-UP shooter owns the payoff) + SHARPSHOOTER;
- `Siegebreaker` → RANGER (the flanker digs campers out) + ASSAULT.
"Many appear in several lines" is the table's own rule. The autopilot already prices all three
(PerkValue, Game.Autopilot.cs:36-57), and `SIGHTLINE_PERK=VNT|BRK|SGE` probes them (PerkDef.Parse,
Unit.cs:807-814).

**Save-compat checklist:** Contract append + BOTH tail pins updated (SaveGame.cs:675,
Game.Harness.cs:1898-1901); `RunDto.PendingSalvageReward` append-only (old saves default 0) +
SAVETEST leg; EventOutcomeKind never persisted (verified); event ids never renamed / arms never
reordered (the compass keys "id:arm"); catalog growth shifts IndexForNode for in-flight unvisited
"?" nodes (accepted skew, no corruption — EVENTTEST's determinism legs compare two regenerations
under the SAME catalog and still pass).

**Verify.** Release 0/0. EVENTTEST extended (a mutation leg per new kind; CureScar's MaxHp revert;
PendingSalvageReward round-trip via the §4 save leg; GrantPrep sets PrepFaction; seeded GrantScar
stable across double-apply). SAVETEST (new field + contract tail). CONTRACTTEST (All == 5 + new
tail pin). METATEST-style legs: MRC halves the ConfirmDraft charge exactly once + skips enshrine;
LGD pension pays once at run end; RemoveVeterans erases exactly the fallen names. CODEXTEST (new
contract rows auto-iterated). `SIGHTLINE_CONTRACT=mrc|lgd` autoplay x3 each (Parse extended) —
no exceptions / no TIMEOUT. Screenshots: `SIGHTLINE_EVENT=1` (+ a new `SIGHTLINE_EVENTID=<id>`
stager in DebugEvent, Game.Harness.cs:2904) for three new events incl. one greyed illegal arm;
`SIGHTLINE_DRAFT=1` showing six contract cards fitting + an MRC-discounted recall bill.
`SIGHTLINE_BALANCE=20` h0: BY EVENT-CHOICE lists the new ids, BY PERK shows VNT/BRK/SGE offered >0,
completion within ±5 of reference (events are off-mission and contracts are opt-in/None-inert, so
base balance is byte-safe by construction — assert the None invariant explicitly).

**Effort:** M (data-heavy; the only new machinery is PendingSalvageReward, RemoveVeterans, the
recall discount and two small outcome executors). **Files:** src/Events.cs, src/Run.cs,
src/SaveGame.cs, src/Game.cs, src/Game.Meta.cs, src/Hud.cs, src/Game.Harness.cs, src/Program.cs
(+ docs/ROADMAP.md, docs/DEVLOG.md, docs/FEATURES.md; src/Codex.cs and src/Stats.cs need no code —
verify only).
