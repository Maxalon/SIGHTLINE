# PROGRAM "PARALLAX" — docket (opened 2026-09-02 on base `cee3cba`)

**Thesis.** Eleven programs tuned SIGHTLINE by win-rate. PARALLAX looks at the same game from
nine angles at once — second-to-second feel, the loop, the instrument, defects, UX, content,
audio/visual, the distributable, code health — and works the items that are visible only when the
views are combined: the most frequent action in the game (a walk) stutters; two of the four modes
never see the bestiary; the settings card could not be reached before a fight; the instrument
compresses the top of the ladder it is used to diagnose. Research: seven lenses, 73 evidenced
findings (`docs/plans/PARALLAX-research.json` holds them verbatim; two lenses — defect-hunt and
ux-accessibility — were lost to an account limit and are re-run as budget allows).

**Team shape.** Lead (orchestrator) + per wave: one developer in an isolated worktree
(`/home/user/wt/<wave>`, branch `wave/<wave>`), one adversarial reviewer, the full
`qa-sweep.sh --full` as tester, lead merges to the working branch, then to `main` via PR.
**Pacing rule learned the hard way:** more than ~3 concurrent agents trips the account limit
mid-wave; run one wave at a time, two at most.

**Rules that do not move:** NO CI / no test framework; every fix ships a hook proven to FAIL
pre-fix and routed through `verdict`; append-only enums; generator draw order is save format;
balance levers one per CRN-paired round with n and base commit stated; no numbers not re-measured.

## Waves, in order (S/M = cost; conf = research confidence 1-5)

| # | wave | goal | source findings | cost | status |
|---|---|---|---|---|---|
| P1 | **SETTINGS EVERYWHERE** | settings card + field manual reachable from INTRO and BARRACKS; `SETTINGSTEST` | ux (C5 open item) | M | in review |
| P2 | **THE STRIDE** | walks glide (segment easing), VAULT arcs, floating text stops overprinting; `FEELTEST` makes pillar 2 gateable | player-feel 0,4,5,11 | M | in dev |
| P3 | **THE FRONT DOOR** | DEPLOY SQUAD gets its [ENTER] chip; cold-profile copy (the resting sentence points at TRAINING OP; "MAX UNLOCKED: 0" reads as a state, not a number); README controls/class tables re-derived from the bound key set | product-ship 0,3,4,5; code-health 6 | S | queued |
| P4 | **THE MODES GET THE BESTIARY** | SKIRMISH and DAILY field the full roster (roster tier decoupled from the stat bump), a faction (dial on the skirmish card; seed-derived for DAILY), pods of 3 and the mid-boss kit at heat >= 4; campaign byte-identical (PAIRTEST + inert_diff) | content-breadth 0 | M | queued |
| P5 | **THE BEAT** | the mission-ending kill plays in slow-mo (TimeScale) instead of a 0.4 s freeze whose zoom-punch decays invisibly; an overwatch REACTION has its own wind-up, flash and cue; an EXPLOSION cue exists (grenade/barrel/siege stop playing crit+death); heal/hack/beacon stop playing reload; `over`/`reload`/`turn` stop carrying 14/15/every meanings | player-feel 1,2,3; audio-visual 0 | M | queued |
| P6 | **THE HEAT PIN AND L5** | `EventCatalog.HeatPinned` for measured batches (+ `RunRec.HeatEnd`, a `heatLeak` block), per-slot rows for single-policy batches, STALEMATE cause split (mission cap vs run cap); then a 16-slot-set rung script and the h6 2^3 factorial (MIDTOOTH x AIDECLINE x BIOMEMECH) — ~10 min of compute that resolves the +5.8 interaction or retires it | balance-instrument 0,1,2,3,8 | M | queued — lands BEFORE any lever |
| P7 | **THE FORK IS A DECISION** | EVENT-node hover no longer names an objective it does not have; the biome/ground rule shows on the fork; PITCHED nodes priced (MissionNode.Intel sees the class); SUPPLY no longer strictly dominates COMBAT (its full heal cancelling the wound gauge is named or trimmed) — reward changes CRN-paired, ladder shown unmoved outside noise | loop-design 0,1,2,3 | M | queued |
| P8 | **STANCE AND GOLD** | stance states (OW/BRC/R&G/BLZ/AIM/SUPP/ROUT/WVR) become >= 12px glyph+word badges with shape, not hue; enemy HUNKER stops wearing the friendly Good colour; the three gold roles (objective / suspicious enemy / VIP) separate | audio-visual 3,4; player-feel 10 | M | queued |
| P9 | **CRASH FILE AND WINEXE** | unhandled exception writes `crash-<version>-<utc>.txt` via the atomic writer (+ `CRASHTEST`); Windows build links the GUI subsystem, publish.sh checks the PE header from Linux | product-ship 1,2 | M | queued |
| P10 | **THE CONTRACT READS TRUE** | CLAUDE.md contradictions at the points a fresh session reads first (file map, duplicated paragraphs, stale free-key line, dial-registry grep); qa-sweep launches TUTTEST/THREATTEST twice; COVERAGE GUARD counts hook names in comment lines; move banner-delimited subsystems out of Game.cs into partials (mechanical, PAIRTEST-proven) | code-health 0,1,2,3 | S-M | queued |
| P11 | **THE ELITE IS WHERE THE MAP SAYS** | mid-boss keyed on the ELITE node, not on mission number 3/5; SUPPLY never fields one; Elite card EnemyDelta 2 -> 1 to pay for it — world-changing, so CRN-paired round + byNodeKind cross-tab | content-breadth 1 | M | queued (after P6) |
| P12 | **MUSIC KNOWS DANGER** | combat intensity keyed to contact/threat, not to "it is the enemy turn"; the win/lose jingle+stinger clash resolved; AUDIOGATE stack list covers the real pairs | audio-visual 1,2 | S-M | queued |
| P13 | **VOID RIFT / ARID SAND** | a fourth and fifth mechanical biome, symmetric by construction (Grid.CostMap / HasLineOfSight), each priced CRN-paired | content-breadth 3,8 | M each | queued |
| P14 | **THE CAMPING POLICY** | a third flywheel policy that turtles, so a turtle exploit is measurable | balance-instrument 4 | M | queued (after P6) |
| P15 | **THE TOP OF THE LADDER** | `Heat.MidTooth` modes 1/2 on the composed tree, 16 slot sets, then the L5 ladder of record | balance-instrument 5; ROADMAP top item | S compute | queued (after P6, P14) |

**Rejected / deferred with a reason.** K-weighted audio budget (instrument for a mix nobody here
can hear); window icon (nice, low impact); biome-shuffled deal (world-changing for a variety gain
the flywheel cannot see — after P13); Program.Main extraction (mechanical but 1,400 lines of
risk for no player-facing gain); grenade band constant (fold into P10 if cheap).

**Definition of done for a wave:** Release 0/0; new hook FAIL pre-fix / PASS post; `qa-sweep.sh
--full` exit 0 with equal derived counts and an empty COVERAGE GAP; PAIRTEST byte-identical for
presentation-only waves; screenshots inspected; DEVLOG section + ROADMAP + FEATURES; merged to
`main` by the lead.
