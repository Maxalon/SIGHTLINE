using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

// Headless AUTOPILOT — the balance/smoke-test AI (SIGHTLINE_AUTOPLAY / SIGHTLINE_SMARTPLAY /
// SIGHTLINE_BALANCE). Nothing here renders; every method drives real player actions so the
// whole loop can be exercised headlessly. Two policies live side by side:
//   - AutoStep()  : the deliberate path-coverage smoke test (nearest target, march in, random
//                   ability/grenade rolls — loses almost everything). The default under AUTOPLAY.
//   - SmartStep() : the COMPETENT autopilot that plays to WIN (cover/threat-aware positioning,
//                   best-target selection, deliberate ability/ambush use) so headless games are a
//                   real balance gauge. Routed to via SMARTPLAY / the balance runner.
// The public entry FLAGS (AutoPlay/SmartPlay/SmartSloppy), the Slip()/SeedSloppy perturbation
// gate, and the anti-stall FIELDS (_lastTelemetryTurn/_autoSig/_autoStall/_smartConcealTurns)
// stay in Game.cs (they're read from the reset/turn-flow code too).
// This is a pure mechanical slice of Game.cs — no behaviour change.
public partial class Game
{
    // ── APEX W4 (d): the flywheel's perk VALUE prior (a small class+kit table) ──────────
    // Consumed by ChoosePerk (Game.cs) under SmartPlay: the pick is biased 70/30 toward the
    // higher-valued slot instead of always taking slot A (= MakePerkOffer's class-line slot).
    // "Class" enters through the KIT — every class carries a fixed weapon kind (ASSAULT=Rifle,
    // RANGER=Shotgun, SHARPSHOOTER=Sniper, CORPSMAN=Smg, GUNNER=Lmg) — plus one explicit
    // class term (the CORPSMAN's value is staying upright to support, not DPS). This is a
    // heuristic PRIOR (competent-play proxy), not ground truth: the telemetry it unlocks —
    // real exposure of both offer slots — is the point. Balance-harness only; never drives
    // interactive play.
    static float SmartPerkValue(Unit u, Perk p)
    {
        WeaponKind k = u?.Weapon != null ? u.Weapon.Kind : WeaponKind.Rifle;
        bool close = k == WeaponKind.Shotgun || k == WeaponKind.Smg;   // short-range kit
        bool anchor = k == WeaponKind.Lmg;                             // overwatch/suppression kit
        float v = p switch
        {
            Perk.Adrenal => 8f,                                          // action economy is king
            Perk.LockOn => 7f,                                           // the bot hunts flanks
            Perk.Gunslinger => k == WeaponKind.Sniper ? 5f : 7f,         // double-tap wants clip/ROF
            Perk.Tank => 6f,
            Perk.Sprinter => 6f,                                         // mobility + overwatch immunity
            Perk.Executioner => 6f,                                      // finisher on focus-fire targets
            Perk.CloseQuarters => close ? 8f : 4f,                       // aim where the kit fights
            Perk.Marksman => k == WeaponKind.Sniper ? 8f : (close ? 2f : 5f),
            Perk.GiantSlayer => 5f,                                      // alpha-strike opener
            Perk.Skirmisher => close ? 6f : 5f,                          // shoot-then-slip suits closers
            Perk.Hardened => 5f,
            Perk.Bulwark => 5f,
            Perk.Guardian => anchor ? 6f : 4f,                           // overwatch perks suit the anchor
            Perk.Reflexes => anchor ? 6f : 4f,
            Perk.CoolHeaded => 4f,
            Perk.Bandolier => 4f,
            Perk.Vantage => 3f,                                          // conditional-crit tail
            Perk.Breaker => 3f,
            Perk.Siegebreaker => 3f,
            _ => 3f,                                                     // cut/unknown (never offered)
        };
        // support survivability: the medic-adjacent class buys durability over damage
        if ((u?.Cls ?? "") == "CORPSMAN" && (p == Perk.Hardened || p == Perk.Tank || p == Perk.CoolHeaded)) v += 2f;
        return v;
    }

    void SmartStep()
    {
        if (_anims.Count > 0 || Phase != Phase.PlayerTurn) return;
        // Decision-richness + swing telemetry: record ONCE at the first idle SmartStep of each
        // player turn (balance harness only; Stats early-outs unless Enabled). Cheap + read-only.
        if (Stats.Enabled && _turnCount != _lastTelemetryTurn)
        {
            _lastTelemetryTurn = _turnCount;
            int choices = CountMeaningfulChoices(out int actingSoldiers, out int armedSoldiers,
                                                 out int losTargets, out int tgtChoices, out int posChoices);
            Stats.RecordPlayerTurn(choices, CurrentLead(), actingSoldiers, armedSoldiers,
                                   losTargets, tgtChoices, posChoices);
        }
        TryFreeCaptive();                       // free a captive a soldier already stands next to
        var u = Players.FirstOrDefault(p => p.CanAct);
        if (u == null) { EndPlayerTurn(); return; }
        Selected = u;
        RecomputeMoveCost();

        // ── SIEGE response (non-shoot tactical axis): a soldier standing in a charged strike zone
        // must (a) interrupt the artillery if it has a good shot at it (PriorityWeight 24 already
        // biases TakeBestShot toward the BOMBARD), else (b) step OUT of the zone. This makes the
        // autopilot exercise the intended "relocate / focus the source" play so balance reflects it,
        // and the SmartFleeSiege fall-through always reaches a guaranteed-progress action (no TIMEOUT).
        if (!SquadConcealed && InSiegeZone(u.X, u.Y))
        {
            if (!Slip(15) && TakeBestShot(u)) return;   // interrupt: kill the charging SIEGE if we can
            if (SmartFleeSiege(u)) return;              // vacate: step to the best out-of-zone tile
            // penned in (rare): fall through to normal routing -> always ends in DoHunker.
        }

        // ── CONCEALMENT / AMBUSH (4.4): spend the opener deliberately ──────────────────
        // While concealed the squad can reposition freely AND pods can't wake by sight, so
        // concealment is a HUGE asset for the "race" objectives: keep stealth and walk the
        // goal (the fragile VIP can cross the map untouched; an Evac squad can slip into the
        // zone). For combat objectives, hold the break until a soldier has a GOOD ambush shot
        // (fire it next step with +20 aim/+25 crit), or until proximity forces the reveal.
        if (SquadConcealed)
        {
            // Stealth-race objectives: ones we approach hidden without needing to fire first.
            // Evac/Escort/Rescue qualify because they're pure "reach a tile" goals (no shot at
            // all while hidden). Hack/Sabotage qualify for the covert APPROACH — but the first
            // hack/plant now GOES LOUD (DoHack breaks concealment, balance fix), so after that
            // the squad drops into the normal combat objective routing to hold & finish. We
            // still creep in concealed (safe approach) rather than ambush-opening from afar.
            // (Eliminate/Decapitate/Defend inherently require killing, so they ambush instead.)
            bool stealthRace = Objective == Objective.Evac || Objective == Objective.Escort
                            || Objective == Objective.Rescue || Objective == Objective.Hack
                            || Objective == Objective.Sabotage;

            // auto-reveal is imminent (a soldier is about to step inside RevealRange of an
            // active foe): break NOW so the ambush bonus isn't wasted on a forced reveal.
            bool forced = Players.Any(p => p.Alive && Enemies.Any(e => e.Alive && e.Active
                    && Util.TileDist(p.X, p.Y, e.X, e.Y) <= RevealRange + 1));

            if (!stealthRace)
            {
                // COMBAT objective: a worthwhile ambush shot from where this soldier stands?
                // W10 SUPPRESSOR: thread the intended ambush target through so a suppressed
                // shooter's break narrows the pod wake exactly like the interactive path.
                var (ambTgt, ambVal) = BestShotFrom(u, u.X, u.Y);
                if (ambTgt != null && ambVal >= 8f)
                { BreakConcealment(u, ambTgt, u.HasMod(WeaponMod.Suppressor)); return; }
                if (forced) { BreakConcealment(); return; }
                // creep into a better firing position before tipping our hand; hold if none.
                if (SmartApproach(u)) return;
                DoHunker(); return;
            }

            // STEALTH-RACE objective: stay hidden and make objective progress (NO shooting —
            // a shot would break stealth and wake the pods we're sneaking past). Only break if
            // a reveal is forced anyway (then spring it with the best-positioned soldier).
            // HARD ANTI-TIMEOUT CAP: if the covert plan ever drags on (squad can't consolidate
            // in the zone / path to the objective is blocked), abandon stealth so the normal
            // combat objective logic — which has bulletproof guaranteed-progress fallbacks —
            // resolves the match. This is the safety net that makes the stealth plan TIMEOUT-proof.
            if (forced || _smartConcealTurns >= 30)
            {
                var breaker = BestSquadAmbushUnit() ?? u;
                Selected = breaker; BreakConcealment(breaker); return;
            }
            if (SmartConcealedRace(u)) return;     // move the VIP/squad toward the goal, hidden
            DoHunker(); return;
        }

        // W10 INTEL CACHE: an opportunistic, bounded detour — the CLOSEST soldier peels off for
        // the cache when it's live and near. Dead code without a cache (CachePresent is campaign-
        // only), one soldier at a time, and hard-bounded by the cache's own expiry clock, so it
        // can never stall a match. This is how the flywheel models the pickup at all (ACTION MIX
        // "INTEL"); without it the smoke AI would only ever collect by accident.
        if (CachePresent && u.ActionsLeft > 0 && !u.IsVip)
        {
            float dCache = Util.TileDist(u.X, u.Y, CacheX, CacheY);
            bool nearest = !Players.Any(p => p.Alive && !p.IsVip && p != u
                                && Util.TileDist(p.X, p.Y, CacheX, CacheY) < dCache);
            if (nearest && dCache <= 8f && TryMoveTowardTile(u, CacheX, CacheY)) return;
        }

        // ── FUL-7 LAST LIGHT: the rescue outranks every other verb (objective-agnostic) ──
        // A soldier standing beside a downed squadmate acts on it NOW: a corpsman with the kit
        // ready REVIVES (the Heal executor's Downed arm — a downed Hp-0 ally is always the
        // most-wounded eligible target); anyone else STABILIZEs once (Stabilized gates repeats).
        // EVAC guard (AutoShouldStabilize): with no live corpsman in the squad, freezing the
        // timer of a body far from the zone would freeze Evac's all-in-zone win too — the bot
        // has no drag-chain carry in v1 (accepted, recorded bot-vs-player gap) — so it lets the
        // timer run instead: cold, but bounded and honest.
        if (u.ActionsLeft > 0 && !u.IsVip)
        {
            var downAdj = Players.FirstOrDefault(p => p.Alive && p.Downed && p != u
                                && Util.ChebyDist(u.X, u.Y, p.X, p.Y) <= 1);
            if (downAdj != null)
            {
                if (u.Ability == AbilityKind.Heal && CanAbility(u)
                    && MostWoundedAdjacentAlly(u) is Unit mw && mw.Downed)
                { DoAbility(); return; }                                   // REVIVE (PATCH)
                if (!downAdj.Stabilized && AutoShouldStabilize(downAdj))
                { DoStabilize(); return; }                                 // freeze the timer
            }
        }

        // ── FUL-5: the corpsman's PATCH is OBJECTIVE-AGNOSTIC ──────────────────────────
        // Every objective routine returns before SmartCombatStep for most soldiers, so the heal
        // (step 1 there) was structurally dead on 5 of 8 objectives — PATCH measured ~1 use per
        // ~500 missions with corpsmen demonstrably fielded. A 1-action patch of a genuinely-hurt
        // squadmate en route is good play on ANY objective (they're about to eat the pods the
        // march wakes), and the ≤2-tile approach detour is bounded + Cd-gated. FUL-7: the
        // approach step (TryMoveToPatch) now also closes on DOWNED allies — for EVERY soldier,
        // not just the corpsman (the founding squad has no corpsman and would otherwise have no
        // bot response to a down at all).
        if (u.Ability == AbilityKind.Heal)
        {
            if (PrepAbility(u)) return;        // adjacent ally missing >=3 -> heal it now
            if (TryMoveToPatch(u)) return;     // hurt/downed ally at Cheby 2-3 -> step adjacent for next pass
        }
        else if (Players.Any(p => p.Alive && p.Downed && !p.Stabilized) && TryMoveToPatch(u)) return;

        // ── FUL-5 R5: SMOKE cover is objective-agnostic too — the retreat it protects mostly
        // happens on the march/hold routes the combat brain never sees (R4: ITEM ~1/20
        // campaigns with the probe buried in step 2b). A likely kill still comes first; the
        // 1-charge/mission budget bounds the verb no matter which route probes it.
        if (u.Item == ItemKind.Smoke && u.ItemCharge > 0 && !HasStrongShot(u) && TrySmokeCover(u)) return;

        // ── OBJECTIVE ROUTING (preserved from AutoStep, with smart combat layered in) ──
        switch (Objective)
        {
            case Objective.Evac:    if (SmartEvac(u))    return; break;
            case Objective.Hack:    if (SmartHack(u))    return; break;
            case Objective.Sabotage:if (SmartSabotage(u))return; break;
            case Objective.Escort:  if (SmartEscort(u))  return; break;
            case Objective.Rescue:  if (SmartRescue(u))  return; break;
            case Objective.Defend:  if (SmartDefend(u))  return; break;
            case Objective.Decapitate: if (SmartDecapitate(u)) return; break;
        }

        // ELIMINATE (and the combat-clearing fall-through for every other objective):
        SmartCombatStep(u);
    }

    /// CONCEALED stealth-race movement: advance the acting soldier toward the goal WITHOUT
    /// firing (pods stay dormant while we're hidden, so a covert dash to evac / the cage is
    /// far safer than waking the board). The VIP/captive heads for extraction; escorts head
    /// for the goal too (to be in position when stealth eventually breaks). Returns true if a
    /// move was issued. NEVER calls a shooting/ability path (that would break concealment).
    bool SmartConcealedRace(Unit u)
    {
        // HACK / SABOTAGE: creep to the objective concealed (safe approach), then hack/plant.
        // NOTE: the first hack/plant now BREAKS stealth (DoHack → BreakConcealment, balance
        // fix), so the very next SmartStep frame falls through to the engaged routing
        // (SmartHack/SmartSabotage) which fights to hold the objective and finish it.
        if (Objective == Objective.Hack)
        {
            if (CanHack(u)) { DoHack(); return true; }
            return TryMoveTowardTile(u, Terminal.x, Terminal.y);
        }
        if (Objective == Objective.Sabotage)
        {
            if (CanHack(u)) { DoHack(); return true; }     // plants the nearest adjacent charge
            var site = SabotageSites.Where((s, i) => !SabotageBlown.Contains(i))
                .OrderBy(s => Util.TileDist(u.X, u.Y, s.x, s.y)).FirstOrDefault();
            return site != default && TryMoveTowardTile(u, site.x, site.y);
        }
        // VIP / freed captive: walk to the extraction zone (concealed → no enemy fire, so just
        // beeline; VipAdvance's exposure scoring is moot while hidden). EXCEPTION: on ESCORT the LEASH
        // (LeashVip) owns the asset's movement — it FOLLOWS the squad, so the concealed race must NOT
        // also self-walk it to the corner (the two would fight each turn). Let it hold; the leash steps it.
        if (u.IsVip)
        {
            if (Objective == Objective.Escort) return false;   // leash-owned; hold (caller hunkers)
            if (CaptiveLocked) return false;   // caged: can't move (hunker)
            // W4 (SIGNAL): the FREED captive is leash-owned too (LeashVip's Rescue arm) — a
            // concealed self-race to the corner would fight the leash every turn boundary.
            if (Objective == Objective.Rescue) return false;
            if (EvacZone.Contains((u.X, u.Y))) return false;
            var g = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                            .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
            return g != default && TryMoveTowardTile(u, g.x, g.y);
        }
        // RESCUE escort: rush to spring the still-caged captive (concealed → safe approach).
        if (Objective == Objective.Rescue && CaptiveLocked && Vip != null
            && Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) > 1)
            return TryMoveTowardTile(u, Vip.X, Vip.Y);
        // EVAC (concealed): drop the forward beacon at a mid-field staging point WHILE STILL HIDDEN —
        // DoBeacon is silent (it doesn't break concealment), so the squad opens a near extraction zone and
        // converges there covertly instead of marching all the way to the far corner. This is what actually
        // cuts the empty-walk turns (the open-with-a-covert-approach path is the common one). Fallback corner
        // stays, so it never soft-locks. After it's planted, everyone beelines to the nearest evac tile (the
        // beacon), so the concealed staging point is already the win position when stealth breaks.
        // APEX W8 — ESCORT gets the same covert plant on its STRICTER far-third staging line (CanBeacon
        // additionally enforces the cold-LZ gate, which counts DORMANT pods — a concealed squad can't
        // stage a leash-win beside a sleeping pod).
        if (CanBeacon(u) && DistToEvac(u.X, u.Y) > 2
            && u.X >= (Objective == Objective.Evac ? Grid.W / 2 : Grid.W * 2 / 3))
        { DoBeacon(); return true; }
        // APEX W8 (Escort, still hidden): the strict cold-LZ gate means a plant beside the pod-dense
        // lanes never opens — so CREEP TO A COLD POCKET of the far third and plant there, instead of
        // marching the whole board to the corner. Falls through to the corner race when fully warm.
        // W4 (SIGNAL): freed-state RESCUE creeps the same way (HasBeaconAction only admits Rescue
        // once the captive is freed, so the caged phase still rushes the cage above).
        if ((Objective == Objective.Escort || Objective == Objective.Rescue)
            && HasBeaconAction && !BeaconPlanted && !u.IsVip)
        {
            var spot = EscortBeaconSpot(u);
            if (spot != null && TryMoveTowardTile(u, spot.Value.x, spot.Value.y)) return true;
        }
        // APEX W8: the haul-aboard pull is SILENT (no shot — concealment holds), so an in-zone soldier
        // extracts the leashed VIP / a straggler even mid-stealth. Without this the concealed race had
        // no extract verb at all: a traced m5 escort parked the VIP BESIDE a soldier-walled zone for
        // three turns because every zone-front tile was occupied and nobody could pull it through.
        if (EvacZone.Contains((u.X, u.Y)) && CanExtract(u)) { DoExtract(); return true; }
        // Evac/Escort soldiers: move toward the NEAREST extraction tile (beacon once planted, else corner).
        var ahead = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                            .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
        if (ahead == default) ahead = EvacZone.OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
        if (ahead != default && !EvacZone.Contains((u.X, u.Y)))
            return TryMoveTowardTile(u, ahead.x, ahead.y);
        return false;   // already staged: hold concealed (caller hunkers)
    }

    /// The squad's best-positioned ambusher right now: the soldier whose current tile yields
    /// the highest-value shot on a live foe. Used to pick WHO springs a forced concealment
    /// break so the +ambush bonus lands the biggest hit. Null if no soldier has any shot.
    Unit BestSquadAmbushUnit()
    {
        Unit best = null; float bestVal = 0f;
        foreach (var p in Players)
        {
            if (!p.Alive || !p.CanAct || p.IsVip || p.Ammo <= 0) continue;
            var (tgt, val) = BestShotFrom(p, p.X, p.Y);
            if (tgt != null && val > bestVal) { bestVal = val; best = p; }
        }
        return best;
    }

    // ── balance instrumentation (harness only) ──────────────────────────────────────────

    /// Current HP "lead" = (sum of living soldier HP) − (sum of living, ACTIVE enemy HP).
    /// Dormant pods don't count (they aren't in the fight yet). Sign-changes in this across a
    /// match = lead swings (a tension proxy); fed to Stats.RecordPlayerTurn.
    int CurrentLead()
    {
        int p = Players.Where(x => x.Alive && !x.IsVip).Sum(x => x.Hp);
        int e = Enemies.Where(x => x.Alive && x.Active).Sum(x => x.Hp);
        return p - e;
    }

    // ── TRUE BAND — the choice-band RULE (SIGHTLINE_CHOICEBAND=mult restores the old one) ──
    // Both axes of CountMeaningfulChoices ask the same question — "how many candidates are
    // near-best?" — and until this wave both answered it with a MULTIPLICATIVE window: within
    // 12% of the best shot value, within 15% of the best safety score. That makes the window
    // WIDTH a function of the best score's magnitude, and on axis (b) the best safety score
    // `24 − TileExposure + cover*8 + height*5` FALLS as the board gets dangerous. So raising
    // threat shrank the absolute window `0.15 × pbest` and disqualified tiles: the instrument
    // read "the fight got safer" as "the decision got richer". Axis (a) rises with threat and
    // axis (b) fell with it, and the sum sat at 1.55-1.64 across five structurally different
    // levers (W4) and 1.44-1.78 across six difficulty rungs (X2) — a near-invariant that made
    // the decision-density gate structurally unreachable, and that two waves missed against.
    // MEASURED CORRECTION (TRUE BAND, SIGHTLINE_BANDPROBE, docs/measurements/tb/): that
    // MECHANISM is not what happens on this tree. `pbest` does NOT slide with threat — it is
    // pinned at the "full cover, unexposed, ground level" value of 40 (24 + 2*8) at every rung
    // (median 40 at heat 0, 4 and 8 — confirmed in ALL FIVE measured chunks), because the
    // autopilot almost always has one such tile in one-action reach. The multiplicative window
    // was therefore already ~6 points wide everywhere, and the `pbest > 0` guard fired on
    // 0.0-1.0% of soldier-turns. (The MEAN also rises with heat on the common CRN base 50 —
    // 37.7 / 38.6 / 38.8 — but that 1.1-point rise reverses off-base, 35.4 at base 60 and 36.6
    // at base 70, so it is world-set noise and is not claimed. The pinned MEDIAN is the
    // load-bearing fact and it is unaffected.) What actually flattens the axis is the CAP, and what makes the window
    // wrong is that it is a fraction of a number whose own scale is arbitrary. Both are fixed
    // here anyway: an ADDITIVE band means a candidate is a real alternative when it scores
    // within a FIXED number of points of the best, so the window is stated in the units the
    // score is actually made of (cover 8, elevation 5, a gun 6-10) instead of in a percentage
    // of a magnitude nobody chose. See DEVLOG §TRUE BAND for the tables and the honest
    // accounting of which half of this change moved the number.
    // NOTE: these five are `static readonly`, not `const`, on purpose — SIGHTLINE_BANDTEST PINS
    // them by value, and a const would be folded at the comparison so the pin could never fail.
    /// The pre-wave multiplicative rule, kept reproducible for the paired instrument diff.
    /// STRICT: only the exact literals "mult" and "add" (and unset) are accepted. A typo used to
    /// select the NEW rule silently, which is the single worst failure mode this dial has — a
    /// batch labelled "mult" in someone's shell history but measured on "add". `ChoiceBandValid`
    /// is checked at the top of Program.Main, which refuses to run at all on a bad value.
    public static readonly string ChoiceBandEnv =
        Environment.GetEnvironmentVariable("SIGHTLINE_CHOICEBAND");
    public static readonly bool ChoiceBandValid =
        string.IsNullOrEmpty(ChoiceBandEnv) || ChoiceBandEnv == "mult" || ChoiceBandEnv == "add";
    public static bool MultChoiceBand = ChoiceBandEnv == "mult";
    /// The instrument identity, stamped into the SIGHTLINE_BALANCE aggregate JSON so a future
    /// comparison script can REFUSE a cross-instrument diff instead of quietly producing one.
    /// "mult-v1" = every number archived before wave TRUE BAND; "add-v2" = everything since.
    public static string InstrumentTag => MultChoiceBand ? "mult-v1" : "add-v2";
    public static readonly float MultShotFrac = 0.88f;   // pre-wave axis (a): within 12% of the best shot
    public static readonly float MultPosFrac  = 0.85f;   // pre-wave axis (b): within 15% of the best safety
    /// Axis (a) additive band, in SHOT-VALUE points. Axis (a) needed the same treatment and the
    /// evidence is its own: the best shot value spans p5=2-4 to p95=20-25 (median 11-13), so the old
    /// 12% window ran 0.36 points wide against a weak best shot and 3.0 wide against a strong
    /// one — it WIDENED exactly when one target was obviously best, which is backwards. The gap
    /// to the best is bimodal (p50=0-1, p75=4-5, p95=9-13): a rival is either a near-duplicate
    /// or a different proposition. 2 points admits the duplicates without admitting a shot a
    /// whole finisher/flank bonus (4-14) away, and it lands near the old window at the MEDIAN
    /// best (0.12 x 11 = 1.3), so the axis-(a) level barely moves — the point is that both axes
    /// now band on the same terms, not that this one changes.
    public static readonly float ShotBand = 2f;
    /// Axis (b) additive band, in SAFETY points. The safety scale's own units are the tactical
    /// steps: one cover level = 8, one elevation step = 5, one exposed enemy gun = 6-10. 3 points
    /// is deliberately BELOW all three — a tile inside the band is one you could stand on without
    /// giving up a cover step, an elevation step or eating an extra gun.
    /// HONEST SCOPE: unlike ShotBand, this value is NOT pinned by the measurement, because the
    /// score lives on that lattice and nothing falls in the gap between 3 and 4 — admitted/turn
    /// reads 3.56 / 3.64 / 3.64 / 4.49 for bands 2 / 3 / 4 / 5 at heat 0. **Any value in [3, 5)
    /// is the same instrument.** 3 is the bottom of that interval, picked for the "same tactical
    /// class" reading. Only 2 (drops the gap-3 tiles) and 5 (admits the elevation step) differ.
    public static readonly float PosBand = 3f;
    /// Anti-inflation cap on axis (b). KEPT — a cap is right, because the admitted-count
    /// distribution is hard right-skewed (measured p50=2-3, p75=4-5, p95=7-15) and its long tail
    /// is the open field: a soldier with fifteen identically safe tiles around it faces one
    /// shrug, not fourteen decisions.
    /// THE ARITHMETIC, stated exactly, because the first version of this comment got it wrong:
    /// the cap applies as `Math.Min(cap, admitted - 1)` inside `if (admitted >= 2)`, so cap 2
    /// first BINDS at admitted >= 4 and cap 4 at admitted >= 6. **Neither clips the median turn**
    /// (admitted 2-3) at any rung — cap 2 starts clipping around p70-p75 of the distribution and
    /// cap 4 around p80-p88. The earlier claim that cap 2 "clips the median" was false.
    /// What IS true, and is the reason for the raise: cap 2 retains only **41-55%** of the
    /// uncapped signal (measured 1.082 of 2.640 at heat 0, 1.242 of 2.399 at heat 4, 1.094 of
    /// 1.992 at heat 8, common slot base 50) while cap 4 retains **60-84%**. Losing half of a
    /// metric's dynamic range to a clip that starts inside its third quartile is too much
    /// compression for a number whose whole job is to distinguish states.
    /// It is a distribution argument, not a "which cap moves the number most" argument: three
    /// caps were priced against the heat rungs too, and at n=10/rung the rung ordering flipped
    /// between two different slot bases, so that comparison resolves nothing and is NOT the
    /// reason for this value. See DEVLOG §TRUE BAND.
    public static readonly int PosChoiceCap = 4;

    /// THE BAND itself, factored out so both axes provably apply the same rule and so
    /// SIGHTLINE_BANDTEST can pin it without needing a board. Returns how many of `vals` count
    /// as near-best. `mult` = the pre-wave multiplicative window (cut at `best * frac`);
    /// otherwise the additive band (cut at `best - band`). Pure — no state, no Util.Rng.
    /// `floorAtZero` clamps the ADDITIVE cut at 0. Axis (a) passes true: `ShotValue` is strictly
    /// positive for any legal shot, so an unclamped cut of `best - 2` goes NEGATIVE whenever the
    /// best shot is worth under 2 points (measured p5 = 2-4, so rare but not empty) and then
    /// admits every rival however worthless — a mirror of the magnitude-dependence this wave
    /// removed. Axis (b) passes FALSE and must: safety scores are signed by construction
    /// (`24 - TileExposure + ...`), and clamping there would re-create the `pbest > 0` guard.
    /// NOTE (honest): because every `ShotValue` is > 0 the clamp is arithmetically a NO-OP on
    /// every number this wave measured — [best-2, 0) is empty of candidates — so it documents an
    /// invariant and guards a future signed ShotValue rather than changing anything. It does NOT
    /// fix the underlying wart: at best = 1.5 the band is still 133% of the best. See ROADMAP.
    public static int AdmitNearBest(List<float> vals, float best, bool mult, float frac, float band,
                                    bool floorAtZero = false)
    {
        float cut = mult ? best * frac : best - band;
        if (!mult && floorAtZero && cut < 0f) cut = 0f;
        int n = 0;
        foreach (var v in vals) if (v >= cut) n++;
        return n;
    }

    /// Decision-richness proxy for THIS player turn. Two axes of real choice, summed across every
    /// soldier that can still act:
    ///   (a) WHICH TARGET — how many distinct candidate shots are within `ShotBand` value points
    ///       of the soldier's best shot (a real "who do I shoot?" call, not a forced single option).
    ///   (b) WHERE TO STAND AFTER FIRING (TEMPO) — because the aimed shot no longer ends the turn,
    ///       a soldier that has a shot AND a spare action faces a genuine "where do I end up after
    ///       firing?" bet: how many distinct safe destinations (low exposure / good cover) score
    ///       within `PosBand` safety points of the best. This axis simply DID NOT EXIST before the
    ///       tempo change (firing zeroed the budget), so its contribution is new decision depth,
    ///       not a re-weighting. Capped per soldier (`PosChoiceCap`) so an open map can't
    ///       trivially inflate it.
    /// Reuses ShotValue/ComputeOdds/TileExposure (read-only — never mutates state, never draws
    /// from Util.Rng: SIGHTLINE_BANDTEST pins both). Cheap + bounded.
    int CountMeaningfulChoices() => CountMeaningfulChoices(out _, out _, out _, out _, out _);
    int CountMeaningfulChoices(out int acting, out int armed)
        => CountMeaningfulChoices(out acting, out armed, out _, out _, out _);

    /// X1 — same count, plus the SHOT-GATE decomposition (Stats.MissionRec): `acting` is the
    /// number of soldiers this turn that were alive, able to act and carrying ammo (i.e. eligible
    /// to be scored at all), and `armed` is the subset that actually had a legal shot from where
    /// they stood. Without these, meaningful-choices/turn cannot be read: a lever that lengthens
    /// a fight adds low-contact mop-up turns and shrinks the roster, both of which push the
    /// per-turn average DOWN without making any individual decision poorer. Pure bookkeeping.
    /// W4 — same count again, plus the CHOICE decomposition: `losTargets` is the number of foes
    /// in range+LoS summed over ARMED soldiers (the raw simultaneous-presentation number the
    /// deployment-geometry wave targets), and `tgtChoices`/`posChoices` split the return value
    /// into its (a) which-target and (b) where-to-stand halves (they sum to it by construction).
    /// X1 could measure that choices/ARMED was ~1.5 but not which axis was starved; this can.
    int CountMeaningfulChoices(out int acting, out int armed,
                               out int losTargets, out int tgtChoices, out int posChoices)
    {
        int total = 0; acting = 0; armed = 0; losTargets = 0; tgtChoices = 0; posChoices = 0;
        foreach (var u in Players)
        {
            if (!u.Alive || !u.CanAct || u.IsVip || u.Ammo <= 0) continue;
            acting++;
            // (a) which target — gather the value of every legal shot from where this soldier stands.
            float best = 0f; int comparable = 0;
            var vals = new List<float>();
            foreach (var e in Enemies)
            {
                if (!e.Alive || (e == Vip && CaptiveLocked)) continue;
                if (Util.TileDist(u.X, u.Y, e.X, e.Y) > u.Weapon.MaxRange) continue;
                bool commanding = Grid.HeightAt(u.X, u.Y) - Grid.HeightAt(e.X, e.Y) >= 2;
                if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y, commanding)) continue;
                float v = ShotValue(Combat.ComputeOdds(Grid, u, e), e);
                vals.Add(v);
                if (v > best) best = v;
            }
            if (best <= 0f) continue;                                      // no shot -> no shot/positioning decision
            armed++;
            losTargets += vals.Count;                                      // W4: raw simultaneous presentation
            // W1 TRUE INSTRUMENT: the CONTINUOUS form of the same question the band below asks.
            // `second` is the runner-up by ORDER, not by value — consume exactly one instance of
            // the max first, so two targets tied at `best` correctly score gap 0 rather than being
            // read as a lone option. Telemetry only; zero draws, zero mutation. It does NOT read
            // the band, so TRUE BAND's rule and this histogram are independent (lead, at merge).
            {
                float second = 0f; bool tookBest = false;
                foreach (var v in vals)
                {
                    if (!tookBest && v >= best) { tookBest = true; continue; }
                    if (v > second) second = v;
                }
                Stats.RecordShotGap(best, second);
            }
            comparable = AdmitNearBest(vals, best, MultChoiceBand, MultShotFrac, ShotBand, floorAtZero: true);
            if (comparable >= 2) { total += comparable - 1; tgtChoices += comparable - 1; }  // real target alternatives
            if (ChoiceProbe.On)
            {
                ChoiceProbe.ShotTurns++; ChoiceProbe.ShotCands += vals.Count;
                ChoiceProbe.Score(ChoiceProbe.ShotBest, best);
                foreach (var v in vals)
                {
                    ChoiceProbe.Gap(ChoiceProbe.ShotGap, best - v);
                    if (v >= best * MultShotFrac) ChoiceProbe.ShotMult++;
                    if (v >= best - 1f) ChoiceProbe.ShotAdd1++;
                    if (v >= best - 2f) ChoiceProbe.ShotAdd2++;
                    if (v >= best - 3f) ChoiceProbe.ShotAdd3++;
                    if (v >= best - 4f) ChoiceProbe.ShotAdd4++;
                }
            }

            // (b) where to stand after firing — only when the soldier can fire AND still has an
            // action left to move (the post-shot positioning bet). Score each 1-action-reachable
            // tile by safety (low exposure) + cover + height; count distinct near-best destinations.
            if (u.ActionsLeft >= 2)
            {
                float SafetyAt(int x, int y)
                {
                    float s = 24f - TileExposure(u, x, y);
                    var foe = AliveEnemies().OrderBy(en => Util.TileDist(x, y, en.X, en.Y)).FirstOrDefault();
                    if (foe != null) s += Grid.GetCover(x, y, foe.X, foe.Y).Level * 8f;
                    s += Grid.HeightAt(x, y) * 5f;
                    return s;
                }
                var pcost = Grid.CostMap(u.X, u.Y, (x, y) => IsOccupiedByOther(x, y, u), out _, u.MoveBudget * 2);
                float pbest = SafetyAt(u.X, u.Y); var pvals = new List<float> { pbest };
                for (int x = 0; x < Grid.W; x++)
                    for (int y = 0; y < Grid.H; y++)
                    {
                        if (pcost[x, y] <= 0 || pcost[x, y] > u.MoveBudget) continue;  // 1-action steps only
                        float s = SafetyAt(x, y); pvals.Add(s); if (s > pbest) pbest = s;
                    }
                if (ChoiceProbe.On)
                {
                    ChoiceProbe.PosTurns++; ChoiceProbe.PosCands += pvals.Count;
                    if (pbest <= 0f) ChoiceProbe.PosSkippedGuard++;
                    ChoiceProbe.Score(ChoiceProbe.PosBest, pbest);
                    int a3 = 0, am = 0;
                    foreach (var s in pvals)
                    {
                        ChoiceProbe.Gap(ChoiceProbe.PosGap, pbest - s);
                        if (pbest > 0f && s >= pbest * MultPosFrac) { ChoiceProbe.PosMult++; am++; }
                        if (s >= pbest - 2f) ChoiceProbe.PosAdd2++;
                        if (s >= pbest - 3f) { ChoiceProbe.PosAdd3++; a3++; }
                        if (s >= pbest - 4f) ChoiceProbe.PosAdd4++;
                        if (s >= pbest - 5f) ChoiceProbe.PosAdd5++;
                    }
                    ChoiceProbe.AdmitAdd3[Math.Min(ChoiceProbe.AdmitMax, a3)]++;
                    ChoiceProbe.AdmitMult[Math.Min(ChoiceProbe.AdmitMax, am)]++;
                    if (a3 >= 2)
                    {
                        ChoiceProbe.PosRawAdd3 += a3 - 1;
                        ChoiceProbe.PosCap2Add3 += Math.Min(2, a3 - 1);
                        ChoiceProbe.PosCap4Add3 += Math.Min(4, a3 - 1);
                        ChoiceProbe.PosAddCap2 += Math.Min(2, a3 - 1);
                        ChoiceProbe.PosAddCap4 += Math.Min(4, a3 - 1);
                    }
                    // the mult leg carries the pre-wave `pbest > 0` guard, so MultCap2 is
                    // EXACTLY the pre-wave contribution on these same soldier-turns.
                    if (pbest > 0f && am >= 2)
                    {
                        ChoiceProbe.PosMultCap2 += Math.Min(2, am - 1);
                        ChoiceProbe.PosMultCap4 += Math.Min(4, am - 1);
                    }
                }
                // MULT mode keeps the pre-wave `pbest > 0` guard, because a non-positive best
                // makes `MultPosFrac * pbest` DEGENERATE (for pbest<0 the cut sits ABOVE pbest,
                // so nothing at all qualifies) — the old rule had to skip the axis outright. The
                // additive band has no such degeneracy and needs no guard. Measured, the guard is
                // nearly inert anyway (2/210 soldier-turns at heat 0, 0/91 at heat 8), so dropping
                // it is not what moves the number; it goes because it is a threat-coupled
                // disqualifier that silently deletes the axis exactly when the board is worst.
                if (!MultChoiceBand || pbest > 0f)
                {
                    int pComparable = AdmitNearBest(pvals, pbest, MultChoiceBand, MultPosFrac, PosBand);
                    if (pComparable >= 2)
                    {
                        int add = Math.Min(MultChoiceBand ? 2 : PosChoiceCap, pComparable - 1);
                        total += add; posChoices += add;                                // capped: anti-inflation
                    }
                }
            }
        }
        return total;
    }

    /// Coverage hook (balance harness): occasionally throw the soldier's utility item when it's
    /// clearly worthwhile and cheaply decidable, so item balance stops being invisible to the
    /// flywheel. Conservative (never wastes a charge on a bad throw, never frags an ally) and
    /// always SAFE (returns false unless it actually issued a throw — progress is guaranteed by
    /// the caller's fallbacks). Items end the turn, so this is a deliberate commitment.
    ///   SMOKE       — drop on a soldier badly exposed to 2+ guns (deny the enemy's sightlines).
    ///   FLASH       — lob onto a cluster of 2+ active foes (disorients them; no ally in blast).
    ///   INCENDIARY  — same cluster test (lays a fire field that denies ground + ignites foes).
    ///   BARRICADE   — drop adjacent cover when the soldier is exposed and has nothing to shoot.
    /// FUL-5 — SMOKE the wounded retreat: cover the most-EXPOSED sub-half-HP squadmate in throw
    /// range (self included). One exposed gun on a sub-half body is already a lethal-risk turn,
    /// so the bar sits far below the self-pin case (>=2 guns); the 1-charge/mission budget
    /// self-bounds it, which is what makes the objective-agnostic SmartStep probe safe. The old
    /// ">=2 guns AND no shot AND it's me" conjunction measured ITEM at 0 per ~500 missions.
    bool TrySmokeCover(Unit u)
    {
        if (u.Item != ItemKind.Smoke || u.ItemCharge <= 0 || u.ActionsLeft <= 0) return false;
        Unit coverAlly = null; float worstExp = 6.9f;   // ~one exposed gun (6 + prio*0.5)
        foreach (var p in AlivePlayers())
        {
            if (p.IsVip || p.MaxHp <= 0 || p.Hp * 2 > p.MaxHp) continue;
            if (Util.TileDist(u.X, u.Y, p.X, p.Y) > ItemRange) continue;
            float pexp = TileExposure(p, p.X, p.Y);
            if (pexp > worstExp) { worstExp = pexp; coverAlly = p; }
        }
        if (coverAlly == null || !ItemTargetOk(u, coverAlly.X, coverAlly.Y)) return false;
        Selected = u;   // IssueItem acts on Selected
        IssueItem(coverAlly.X, coverAlly.Y);
        return true;
    }

    bool TrySmartItem(Unit u)
    {
        if (u.ItemCharge <= 0 || u.Item == ItemKind.None || u.ActionsLeft <= 0) return false;
        Selected = u;   // IssueItem acts on Selected (already set by SmartStep, set again defensively)

        switch (u.Item)
        {
            case ItemKind.Smoke:
            {
                // FUL-5 R5: wounded-retreat cover moved to the shared TrySmokeCover — probed
                // objective-agnostically from SmartStep (this combat-brain path kept it at ~0:
                // the R4 reading — most smoke moments live on the march/hold routes, and a
                // carrier with any shot fired instead). Try it here too for the Eliminate case.
                if (TrySmokeCover(u)) return true;
                // original self-pin case: this soldier badly exposed (≥2 guns) with no shot —
                // smoke its own tile to break the lanes.
                if (TileExposure(u, u.X, u.Y) < 12f) return false;
                var (tgt, _) = BestShotFrom(u, u.X, u.Y);
                if (tgt != null) return false;                       // prefer shooting if we can
                if (!ItemTargetOk(u, u.X, u.Y)) return false;
                IssueItem(u.X, u.Y); return true;
            }
            case ItemKind.Flash:
            case ItemKind.Incendiary:
            {
                // lob onto the densest reachable cluster of ACTIVE foes (≥2), never near an ally.
                int bx = -1, by = -1, best = 1;
                foreach (var e in Enemies)
                {
                    if (!e.Alive || !e.Active) continue;
                    if (Util.TileDist(u.X, u.Y, e.X, e.Y) > ItemRange || !ItemTargetOk(u, e.X, e.Y)) continue;
                    if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y)) continue;
                    int foes = Enemies.Count(o => o.Alive && o.Active && Util.ChebyDist(e.X, e.Y, o.X, o.Y) <= 1);
                    bool ally = Players.Any(p => p.Alive && Util.ChebyDist(e.X, e.Y, p.X, p.Y) <= 1);
                    if (ally || foes < 2) continue;
                    if (foes > best) { best = foes; bx = e.X; by = e.Y; }
                }
                if (bx < 0) return false;
                IssueItem(bx, by); return true;
            }
            case ItemKind.Barricade:
            {
                // drop cover when the soldier is exposed and has nothing to shoot — on an
                // adjacent empty floor tile between it and the nearest foe.
                if (TileExposure(u, u.X, u.Y) < 12f) return false;
                var (tgt, _) = BestShotFrom(u, u.X, u.Y);
                if (tgt != null) return false;
                var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
                if (foe == null) return false;
                int dx = Math.Sign(foe.X - u.X), dy = Math.Sign(foe.Y - u.Y);
                int tx = u.X + dx, ty = u.Y + dy;
                if (Util.TileDist(u.X, u.Y, tx, ty) > ItemRange || !ItemTargetOk(u, tx, ty)) return false;
                IssueItem(tx, ty); return true;
            }
        }
        return false;
    }

    // ── objective sub-routines ────────────────────────────────────────────────────────
    // Each returns true once it has committed this soldier's action. They prioritise the
    // OBJECTIVE move but interleave smart combat (best shot / cover) so the squad fights
    // its way to the goal instead of marching straight into fire. Returning false hands the
    // soldier to SmartCombatStep (the shared combat brain + guaranteed-progress fallback).

    bool SmartEvac(Unit u)
    {
        // FORWARD BEACON: the win no longer requires marching to the far corner — a soldier can drop a
        // beacon ONCE and extract the squad there. Plant it as soon as the point man has pushed past the
        // half-way line (a forward, defensible spot), which collapses the long empty walk. CanBeacon
        // already gates it (Evac-only, once/mission, on walkable floor not already an evac tile), and the
        // fixed corner remains as the fallback, so this can never soft-lock the mission.
        if (CanBeacon(u) && u.X >= Grid.W / 2 && DistToEvac(u.X, u.Y) > 2)
        { DoBeacon(); return true; }
        // EXTRACTION IS A RACE: the longer the squad lingers the more pods wake and grind it
        // down (smart positioning that adds turns LOSES Evac). So beeline to the zone FIRST;
        // only fight when genuinely blocked. Exception: take a kill ONLY when it doesn't cost
        // tempo — a near-certain finisher of a foe that already threatens the lane.
        if (EvacZone.Contains((u.X, u.Y)))
        {
            // arrived: haul any adjacent straggler aboard (lift-out — cuts the drag), then hold it.
            if (CanExtract(u)) { DoExtract(); return true; }
            if (TrySmartDrag(u)) return true;   // pull a lagging ally one step closer to the zone
            if (TakeBestShot(u)) return true;
            if (u.Ammo == 0 && u.ActionsLeft > 0) { DoReload(); return true; }
            if (HoldOverwatch(u)) return true;
            DoHunker(); return true;
        }
        // push for the nearest free extraction tile — distance-greedy (speed over cover).
        var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                           .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
        if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return true;
        // couldn't advance this turn (path blocked): clear a blocker, blast a squatter, re-arm.
        if (TakeBestShot(u)) return true;
        if (u.Grenades > 0)
        {
            // blast a squatter loose — but NEVER if a soldier is in the blast (friendly fire);
            // the very "path jammed" state that got us here often means an ally is close by.
            var blocker = AliveEnemies()
                .Where(e => EvacZone.Contains((e.X, e.Y)) && CanGrenade(u, e.X, e.Y) && NoAllyInBlast(e.X, e.Y))
                .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (blocker != null) { IssueGrenade(blocker.X, blocker.Y); return true; }
        }
        if (TrySmartDrag(u)) return true;   // can't advance: at least pull a straggler forward
        if (u.Ammo == 0) { DoReload(); return true; }
        DoHunker(); return true;
    }

    /// True if NO living soldier sits within a grenade's blast (Chebyshev GrenadeAnim.Radius)
    /// of (tx,ty) — the friendly-fire safety check shared by every SmartStep grenade path.
    bool NoAllyInBlast(int tx, int ty)
        => !AlivePlayers().Any(f => Util.ChebyDist(tx, ty, f.X, f.Y) <= GrenadeAnim.Radius);

    /// Chebyshev distance from (x,y) to the nearest extraction tile (0 inside the zone).
    int DistToEvac(int x, int y)
    {
        if (EvacZone.Count == 0) return 0;
        int best = int.MaxValue;
        foreach (var t in EvacZone) best = Math.Min(best, Util.ChebyDist(x, y, t.x, t.y));
        return best;
    }

    /// APEX W8 — nearest COLD far-third staging tile for the Escort forward beacon: walkable floor at
    /// x >= W*2/3, not already an evac tile (or beside one — don't waste the one-per-mission plant),
    /// unoccupied, and no LIVING non-routed enemy within Chebyshev 3 (exactly CanBeacon's cold-LZ
    /// gate). The escort bot ROUTES its point man here instead of beelining the far corner — the
    /// strict gate means a plant near the pod-dense lanes never opens, so the outplay is finding the
    /// quiet pocket. Null when the whole far third is warm (caller falls back to the corner march).
    (int x, int y)? EscortBeaconSpot(Unit u)
    {
        (int x, int y)? best = null; int bestD = int.MaxValue;
        for (int x = Grid.W * 2 / 3; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                if (!Grid.IsFloor(x, y) || EvacZone.Contains((x, y)) || DistToEvac(x, y) <= 2) continue;
                if (IsOccupiedByOther(x, y, u)) continue;
                if (Enemies.Any(e => e.Alive && e.Routed == 0 && Util.ChebyDist(x, y, e.X, e.Y) <= 3)) continue;
                int d = Util.ChebyDist(u.X, u.Y, x, y);
                if (d < bestD) { bestD = d; best = (x, y); }
            }
        return best;
    }

    /// FIELD CRAFT autopilot (W1, bounded): on the march-to-the-corner objectives, if `u` is within
    /// drag reach of a friendly that lags BEHIND it (farther from evac) and DRAGging that ally pulls it
    /// one step CLOSER to evac, do it. The once-per-turn DraggedThisTurn flag bounds it to a single pull,
    /// so it
    /// can never loop; callers still fall through to their normal logic, so it's never the sole stalling
    /// action. Returns true iff a drag was issued.
    bool TrySmartDrag(Unit u)
    {
        if (!CanDrag(u)) return false;
        int myDist = DistToEvac(u.X, u.Y);
        Unit best = null; int bestGain = 0;
        foreach (var a in Players)
        {
            if (!DragTargetOk(u, a)) continue;
            int allyDist = DistToEvac(a.X, a.Y);
            if (allyDist <= myDist) continue;                 // only pull a straggler that's BEHIND us
            int landDist = DistToEvac(a.X + Math.Sign(u.X - a.X), a.Y + Math.Sign(u.Y - a.Y));
            int gain = allyDist - landDist;                   // how much closer the pull lands the ally
            if (gain > bestGain) { bestGain = gain; best = a; }
        }
        if (best != null) { IssueDrag(best); return true; }
        return false;
    }

    /// FIELD CRAFT autopilot (W1, bounded): in combat, if a badly-wounded ally (≤⅓ HP) sits within
    /// drag reach and pulling it toward `u` lands it on a LESS-exposed tile (or at least no worse),
    /// drag it out of the line of fire. Once/turn via DraggedThisTurn; falls through otherwise.
    bool TryRescueDrag(Unit u)
    {
        if (!CanDrag(u)) return false;
        Unit best = null; float bestDrop = 0.01f;          // require a real exposure reduction
        foreach (var a in Players)
        {
            if (a == u || a.IsVip) continue;               // the VIP is handled by the escort brain
            if (a.Hp * 3 > a.MaxHp) continue;              // only genuinely-wounded allies (≤ 1/3 HP)
            if (!DragTargetOk(u, a)) continue;
            int lx = a.X + Math.Sign(u.X - a.X), ly = a.Y + Math.Sign(u.Y - a.Y);
            float drop = TileExposure(a, a.X, a.Y) - TileExposure(a, lx, ly);   // positive = safer landing
            if (drop > bestDrop) { bestDrop = drop; best = a; }
        }
        if (best != null) { IssueDrag(best); return true; }
        return false;
    }

    bool SmartHack(Unit u)
    {
        if (CanHack(u)) { DoHack(); return true; }          // adjacent: hack it down
        // a strong shot is worth taking; otherwise RUSH the terminal (speed limits how many
        // pods wake before we're done — distance-greedy, like the dumb baseline but smarter
        // about when to pause). Once stuck, clear blockers / re-arm.
        if (HasStrongShot(u) && TakeBestShot(u)) return true;
        if (TryMoveTowardTile(u, Terminal.x, Terminal.y)) return true;
        if (TakeBestShot(u)) return true;                   // pinned: clear blockers
        if (u.Ammo == 0) { DoReload(); return true; }
        return false;                                       // hand to combat brain (overwatch/hunker)
    }

    bool SmartSabotage(Unit u)
    {
        if (CanHack(u)) { DoHack(); return true; }          // plant the charge
        if (HasStrongShot(u) && TakeBestShot(u)) return true;
        var site = SabotageSites.Where((s, i) => !SabotageBlown.Contains(i))
            .OrderBy(s => Util.TileDist(u.X, u.Y, s.x, s.y)).FirstOrDefault();
        if (site != default && TryMoveTowardTile(u, site.x, site.y)) return true;
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        return false;
    }

    /// W4 — the SmartEscort lone-VIP self-race counts a DOWNED soldier as fallen (it cannot act,
    /// and the leash skips it as an anchor). Ships ON; SIGHTLINE_ESCORTFIX=0 restores the old,
    /// broken test so the fix can be measured as its own CRN-paired round.
    public static bool EscortDownedFix = true;

    bool SmartEscort(Unit u)
    {
        if (u.IsVip)
        {
            // The LEASH (StartPlayerTurn -> LeashVip) now walks the asset toward the squad each turn —
            // it FOLLOWS the advance instead of being hand-driven. So the VIP's own turn just tucks in
            // (its gun is irrelevant); no more per-soldier micro-walking the fragile asset to the corner.
            // LAST-SURVIVOR FALLBACK: if the whole squad has fallen, there's nobody to follow — the leash
            // holds it in place, which would livelock to the turn cap. So the lone VIP self-races to evac
            // (win if it makes it, else it dies to the foes en route) — either way the match RESOLVES.
            // W4 INSTRUMENT FIX: `Alive` is TRUE for a soldier who is DOWNED and bleeding out, so
            // the old test read "the squad still stands" while every escort lay on the floor — the
            // leash then skipped its downed anchors, the self-race never fired, and the asset just
            // hunkered until the timers expired. Pure drag, and it fires exactly in the apex state
            // where X1 measured Escort at 15.61 turns. This is a MEASUREMENT-INSTRUMENT defect (it
            // makes the bot worse at Escort than a human would be), so it ships ON and the harness
            // pin SIGHTLINE_ESCORTFIX=0 reproduces the old behaviour for the paired round.
            if (!Players.Any(p => p.Alive && (!EscortDownedFix || !p.Downed) && !p.IsVip)
                && !EvacZone.Contains((u.X, u.Y)))
            {
                var g = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
                if (g != default && TryMoveTowardTile(u, g.x, g.y)) return true;
            }
            DoHunker(); return true;
        }
        // escorts: a soldier who has reached the zone hauls the asset (VIP) aboard the instant
        // it's adjacent — the lift-out that ends the escort the moment the VIP is beside the zone.
        if (EvacZone.Contains((u.X, u.Y)) && CanExtract(u)) { DoExtract(); return true; }
        // APEX W8 — FORWARD BEACON (Escort): once the point man has pushed into the far third with a
        // cold LZ (CanBeacon's Escort gate does the real vetting), open the extraction zone HERE — the
        // leashed VIP then converges on the beacon instead of dragging the squad across the whole board
        // to the far corner (the flagged ~14-turn Escort drag). Mirrors SmartEvac's plant verb;
        // DistToEvac > 2 keeps the one-per-mission plant from being wasted beside the existing zone.
        if (CanBeacon(u) && u.X >= Grid.W * 2 / 3 && DistToEvac(u.X, u.Y) > 2
            && Vip != null && Vip.Alive) { DoBeacon(); return true; }
        bool inZone = EvacZone.Contains((u.X, u.Y));
        // Take a FREE finisher on a threat first (a strong shot doesn't cost the advance materially).
        if (HasStrongShot(u) && TakeBestShot(u)) return true;
        // APEX W8 (Escort, engaged): route the advance to a COLD far-third pocket and plant there —
        // the strict cold-LZ gate never opens beside the pod lanes, so the point man aims for the
        // quiet flank instead of the far corner. Cover-aware move; falls through to the corner march
        // (below) when the whole far third is warm, so this can never stall the advance.
        if (HasBeaconAction && !BeaconPlanted && Vip != null && Vip.Alive && !inZone)
        {
            var spot = EscortBeaconSpot(u);
            if (spot != null && SmartMoveToward(u, spot.Value.x, spot.Value.y)) return true;
        }
        if (!inZone)
        {
            // APEX W8 anti-livelock (paired with the vacate valve below): while the leashed VIP is
            // parked BESIDE a nearly-full zone, don't grab the last free evac tile out from under it
            // (a squadmate may have just vacated it) — hold and fight instead, so the leash can walk
            // the asset in at the next turn boundary (the VIP always moves first).
            if (Vip != null && Vip.Alive && !EvacZone.Contains((Vip.X, Vip.Y))
                && DistToEvac(Vip.X, Vip.Y) <= 2
                && EvacZone.Count(t => !IsOccupiedByOther(t.x, t.y, u)) <= 1)
            {
                if (TakeBestShot(u)) return true;
                if (u.Ammo == 0 && u.ActionsLeft > 0) { DoReload(); return true; }
                DoHunker(); return true;
            }
            // FUL-5: pull a straggler along the route (DRAG toward the evac anchor) — SmartEvac
            // has had this pull since W1; the escort march never did, so its slow tail (the LMG,
            // the wounded, the leashed VIP itself at Cheby 2) dragged the leash pace. 1 action,
            // capped per turn by DragsThisTurn, never ends the turn — the advance continues.
            if (TrySmartDrag(u)) return true;
            // ADVANCE to the zone the SAFE (cover-aware) way, not a naked beeline — SmartMoveToward hugs
            // cover / avoids exposure while still closing on the nearest evac tile (and falls back to a plain
            // step so progress is guaranteed). Racing the squad naked into the far corner (which sits in the
            // enemy spawn zone) got soldiers killed → squad wipes → lone-VIP stalemates that ballooned turns.
            var ahead = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
            if (ahead == default) ahead = EvacZone.OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
            if (ahead != default && SmartMoveToward(u, ahead.x, ahead.y)) return true;
            // jammed: clear a blocker / re-arm, then HOLD (never fall through to free-roaming combat — that
            // scatters the escort off the route and abandons the asset mid-field).
            if (TakeBestShot(u)) return true;
            if (u.Ammo == 0) { DoReload(); return true; }
            DoHunker(); return true;
        }
        // APEX W8 anti-livelock: a small (wall-clipped) forward-beacon zone can be FULLY occupied by
        // the squad, leaving the leashed VIP parked adjacent with no free evac tile to step into —
        // soldiers held the zone, the VIP held beside them, and the match stalled to the turn cap.
        // If the VIP is close but has NO free evac tile within reach, VACATE this tile (one step to
        // an adjacent non-zone floor) so the leash walks the asset in next turn — the win follows
        // immediately, so this can never loop.
        if (Vip != null && Vip.Alive && !EvacZone.Contains((Vip.X, Vip.Y))
            && Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) <= 2
            && NearestFreeEvac(Vip.X, Vip.Y, Vip, 2) == null && u.ActionsLeft > 0)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = u.X + dx, ny = u.Y + dy;
                    if ((dx == 0 && dy == 0) || !Grid.IsFloor(nx, ny) || EvacZone.Contains((nx, ny))) continue;
                    if (IsOccupiedByOther(nx, ny, u)) continue;
                    if (TryMoveTowardTile(u, nx, ny)) return true;   // step aside; the leash takes the tile
                }
        }
        // IN the zone, VIP not yet extractable: HOLD the zone (shoot what's in reach, watch, hunker) so the
        // squad stays CONSOLIDATED for the leashed VIP to arrive — do NOT wander off hunting the last foes.
        // FUL-5: first, reel in a Cheby-2 straggler/VIP (drag lands it adjacent -> the extract
        // pull or the leash finishes the job next pass) — the zone-hold turn was otherwise idle.
        if (TrySmartDrag(u)) return true;
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        // FUL-5 R7: probe the brace/focus reads first (HoldOverwatch) — a charger pushing the
        // held zone is exactly the brace case; the plain wide watch stays as the floor.
        if (HoldOverwatch(u)) return true;
        if (u.ActionsLeft > 0 && u.Ammo > 0 && !u.HasStatus(StatusKind.Disoriented)) { DoOverwatch(); return true; }
        DoHunker(); return true;
    }

    bool SmartRescue(Unit u)
    {
        // PHASE 2 (freed) — W4 (SIGNAL): the mission IS an escort now, so run the Escort brain
        // wholesale. The leash (LeashVip) walks the freed captive with the squad, the point man
        // opens the strict-gated forward beacon, soldiers advance/extract/hold the zone, and the
        // captive's old self-race to evac is GONE (it fought the leash — the two tugged the asset
        // in opposite directions every turn). SmartEscort's VIP arm keeps the no-soldiers-left
        // fallback (the lone freed-captive walk-out win HEATLADDERTEST pins), so a shattered
        // squad still resolves. Delegation = de-drag parity with Escort by construction.
        if (!CaptiveLocked) return SmartEscort(u);
        if (u.IsVip) { DoHunker(); return true; }     // caged: can't move
        // PHASE 1 — spring the captive ASAP: the WHOLE squad converges on the cage (the
        // dumb baseline does this and it's right — the captive sits mid-board, so dawdling
        // in cover just lets the enemies mass). Take a free finisher en route, else beeline.
        if (Vip != null && Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) > 1)
        {
            if (HasStrongShot(u) && TakeBestShot(u)) return true;     // a sure kill on the way is fine
            if (TryMoveTowardTile(u, Vip.X, Vip.Y)) return true;      // otherwise rush the cage
        }
        // adjacent already (TryFreeCaptive will spring it next tick): fight from here.
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        return false;
    }

    bool SmartDefend(Unit u)
    {
        // HOLD THE LINE: win = survive N turns, so DON'T wander (every step out of cover is
        // risk and there's nowhere to "go"). Prep a steady shot, fire the best target, then
        // overwatch the approach unconditionally (waves keep coming — a held lane is never
        // wasted), and hunker as the floor.
        if (PrepAbility(u)) return true;
        // FUL-5 R7: waves ARRIVE clustered and dig into cover on approach — the covered-pair
        // frag is textbook defend play, and this routine never reached SmartCombatStep's 2a.
        if (u.Grenades > 0 && !HasStrongShot(u) && SmartGrenade(u, preShot: true)) return true;
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        // FUL-4 co-fix: "stay put" must not mean "die in place" — a defender whose tile is
        // flanked by (or naked to) a live attacker falls BACK to better cover before watching.
        // SmartReposition has no advance pull, so this never wanders off the holdout; without
        // it the measured Defend number was partly the bot refusing to leave a burning tile.
        if (DefendPostureBad(u) && SmartReposition(u)) return true;
        // FUL-5 R7: route the watch through HoldOverwatch FIRST — the defend is where its
        // brace-vs-charger and one-lane FOCUS reads matter most, and the direct DoOverwatch
        // here bypassed both probes. Its pushers-empty early-out falls to the unconditional
        // wide watch below (a held lane is never wasted between waves).
        if (HoldOverwatch(u)) return true;
        if (u.Ammo > 0 && u.ActionsLeft > 0 && !u.HasStatus(StatusKind.Disoriented))
        { DoOverwatch(); return true; }     // always worth watching on a defend
        DoHunker(); return true;
    }

    /// FUL-4: the defender's CURRENT tile is a liability — at least one armed, active foe has an
    /// in-range, in-LoS shot against which the tile gives no working cover (open or flanked).
    /// Cheap gate so SmartDefend only repositions when standing still is actively losing HP.
    bool DefendPostureBad(Unit u)
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Active || e.Ammo <= 0) continue;
            if (Util.TileDist(u.X, u.Y, e.X, e.Y) > e.Weapon.MaxRange) continue;
            if (!Grid.HasLineOfSight(e.X, e.Y, u.X, u.Y)) continue;
            var cov = Grid.GetCover(u.X, u.Y, e.X, e.Y);
            if (cov.Level == 0 || cov.Flanked) return true;
        }
        return false;
    }

    // W8 THE HALF WALL — an INSTRUMENT dial, not a game rule. `SmartDecapitate` below is a hard
    // focus policy: while the HVT lives, every soldier peels its guards, shoots it, grenades it or
    // WALKS AT IT, and ignores the rest of the force except a blocker within 3 tiles. On the
    // finale that is fine — Mission.Build de-stacks the boss force by 3-4 bodies. On a mid-run
    // Decapitate the force is NOT de-stacked (an ELITE node even adds +2 bodies and +1 stat), so
    // the same policy walks a squad across an intact firing line. That makes "the mid-run
    // Decapitate is hard" ambiguous between the GAME and the BOT, and this wave will not publish
    // a number that cannot tell them apart. `SIGHTLINE_HVTPOLICY=0` demotes the HVT to an ordinary
    // target (generic combat: clear what is shooting you, kill the HVT when it is the best shot),
    // EXCEPT when it is the last active hostile, where the focus branch still runs so a batch can
    // never stall. DEFAULT true == today's behaviour exactly.
    public static bool SmartHvtFocus = true;

    bool SmartDecapitate(Unit u)
    {
        if (!SmartHvtFocus && Hvt != null && Hvt.Alive
            && Enemies.Count(e => e.Alive && e.Active) > 1) return false;
        if (Hvt != null && Hvt.Alive)
        {
            // GUARDED HVT (W4): while the HVT shrugs off damage, PEEL its bodyguards first — shoot a
            // living, in-range guard this soldier can hit (bounded: at most 2 guards). If none is
            // reachable, fall through to grind the HVT directly (reduced-not-zero damage → no stall).
            if (Hvt.HvtGuarded && u.Ammo > 0)
            {
                Unit guard = _hvtGuards
                    .Where(g => g != null && g.Alive
                                && Util.ChebyDist(g.X, g.Y, Hvt.X, Hvt.Y) <= Combat.HvtGuardRange
                                && CanTarget(u, g))
                    .OrderBy(g => g.Hp)
                    .FirstOrDefault();
                if (guard != null) { AutoShootSmart(u, guard); return true; }
            }
            // shoot the HVT on sight (prep a steady shot first if it sharpens the kill).
            if (u.Ammo > 0 && CanTarget(u, Hvt))
            {
                if (PrepAbilityFor(u, Hvt)) return true;
                AutoShootSmart(u, Hvt); return true;
            }
            if (u.Ammo == 0) { DoReload(); return true; }
            if (u.Grenades > 0 && CanGrenade(u, Hvt.X, Hvt.Y)
                && !Players.Any(f => f.Alive && Util.ChebyDist(f.X, f.Y, Hvt.X, Hvt.Y) <= 1))
            { IssueGrenade(Hvt.X, Hvt.Y); return true; }       // flush it out of cover
            // can't reach it: drop a close blocker, else maneuver onto the HVT.
            var blk = FirstTargetFor(u);
            if (blk != null && blk != Hvt && Util.TileDist(u.X, u.Y, blk.X, blk.Y) <= 3
                && TakeBestShot(u)) return true;
            if (SmartMoveToward(u, Hvt.X, Hvt.Y)) return true;
        }
        return false;   // HVT dead/unreachable → generic combat
    }

    // ── the shared combat brain (ELIMINATE + every objective's clear-and-advance) ──────
    // The heart of the competent AI. Order of preference for a single soldier:
    //   1. prep a value-adding ability (STEADY before a strong shot, SUPPRESS/SMOKE a
    //      dangerous foe, PATCH a badly-hurt adjacent ally, RUN&GUN/BLITZ for tempo);
    //   2. fire the best expected-value target (finishers + flanks + priority foes first);
    //   3. grenade a 2+ cluster, or a well-covered target we can't shoot well;
    //   4. reposition toward cover / a flanking angle (subtracting tile exposure);
    //   5. reload if dry, overwatch if foes will push, else hunker (always progresses).
    void SmartCombatStep(Unit u)
    {
        // 1 — deliberate ability prep that improves THIS turn's outcome.
        if (PrepAbility(u)) return;

        // 1b — TEMPO (HORIZON W1): if we already fired and haven't moved, weigh ducking to safety
        //      vs a rushed 2nd shot. Ducks to cover ONLY when clearly better (never skips a finisher).
        //      SLOPPY (W2): ~10% forget to duck after firing — the soldier stays EXPOSED BY FIRE
        //      through the enemy turn (the classic post-shot positional error).
        //      FUL-5: NOT when a brace-worthy charger is inbound — cover doesn't stop a committed
        //      charger (it closes to point-blank and flanks past it), the stagger does. Skipping
        //      the duck lets the cascade fall through: 2nd shot if decent (step 2), else BRACE
        //      (5a) — the shoot-then-brace turn the R2 reading showed greedy could never reach.
        if (u.FiredThisTurn && !u.MovedAfterFire && u.ActionsLeft > 0 && !Slip(10)
            && !RusherBraceWorthy(u) && SmartRetreatAfterShot(u)) return;

        // 2a — FUL-5 grenade-first: a 2+ CLUSTER of covered foes beats the gun — the frag deals
        //      ~2x its damage, ignores the cover that is blunting our shots, AND strips it for
        //      the squad's follow-up. Only pre-empts the gun when no likely kill is on the table
        //      (HasStrongShot) and only for covered clusters (SmartGrenade's preShot gate) — an
        //      exposed cluster still gets shot at first (bullets are free, grenades aren't).
        if (u.Grenades > 0 && !HasStrongShot(u) && SmartGrenade(u, preShot: true)) return;

        // 2 — best shot by expected value (only when it's actually worth firing). SLOPPY: ~15%
        //     of the time mis-judge and skip an otherwise-good shot (a human hesitation) — falls
        //     through to a worse action below, so the GAP measures the cost of that error.
        if (!Slip(15) && TakeBestShot(u)) return;

        // 2b — TOY COVERAGE (harness): occasionally commit a utility item when it's clearly the
        //      right call (smoke when pinned in the open, flash/incendiary a cluster, drop cover).
        //      Ends the turn, so it comes after shooting but before the positional fallbacks.
        if (TrySmartItem(u)) return;

        // 3 — out of ammo: reload now so next step can fire.
        if (u.Ammo == 0 && u.ActionsLeft > 0) { DoReload(); return; }

        // 4 — grenade: catch a cluster, or flush a target our gun can't crack.
        if (u.Grenades > 0 && SmartGrenade(u)) return;

        // 4b — FIELD CRAFT (W1): if a badly-wounded ally is within drag reach, pull it one tile
        //      toward us (out of the open / back toward the squad). Bounded once/turn (DraggedThisTurn),
        //      a free repositioning support act; falls through if no good pull exists. (Rarely satisfiable
        //      under the greedy bot's spacing — soldiers cluster at Chebyshev-1, which is non-draggable —
        //      so DRAG is primarily a player tool; this keeps the autopilot path covered when it does align.)
        if (TryRescueDrag(u)) return;

        // 4c — FUL-5 move-to-PATCH: PrepAbility only heals ADJACENT allies, and the greedy bot's
        //      spread means the corpsman almost never happens to stand beside the hurt soldier —
        //      PATCH measured ~1 use per ~500 missions. Close the last step deliberately: with the
        //      kit off cooldown and a genuinely-hurt ally within Cheby 2, step adjacent NOW so the
        //      next pass (or next turn's step 1) lands the heal. A real move — never a stall.
        if (TryMoveToPatch(u)) return;

        // 5a — FUL-5: BRACE vs an inbound charger, decided BEFORE the approach. Reaching this
        //      step means no worthwhile shot exists from this tile (TakeBestShot declined), and
        //      the R1 reading proved HoldOverwatch below is unreachable in open combat —
        //      SmartApproach almost always issues a move first. Advancing INTO a committed
        //      charger wastes that move (it is coming to us either way): hold the disrupting
        //      reaction instead — a landed stagger denies the charger's post-move swing, and
        //      next turn we shoot it point-blank. Sloppy mirrors the forgotten-reaction slip.
        if (!Slip(15) && TryBraceRushers(u)) return;

        // 5 — no shot available this turn: maneuver toward a covered firing position on the
        //     nearest foe (cover + flank − exposure). If we're already well-placed and a foe
        //     is in sight, hold overwatch; otherwise keep closing. Always ends in hunker.
        if (SmartApproach(u)) return;
        // SLOPPY: ~15% of the time forget to set overwatch (a common human omission).
        if (!Slip(15) && HoldOverwatch(u)) return;
        if (SmartReposition(u)) return;     // shuffle into the best adjacent cover if any
        DoHunker();                         // guarantees progress
    }

    // ════════════════════ smart combat helpers ════════════════════════════════════════

    /// ComputeOdds as if `a` stood at (ax,ay) — the Ai.Plan trick (move, compute, restore).
    /// Lets us score a prospective firing tile without actually moving the unit.
    ShotOdds SmartOdds(Unit a, int ax, int ay, Unit d)
    {
        int ox = a.X, oy = a.Y;
        a.X = ax; a.Y = ay;
        var odds = Combat.ComputeOdds(Grid, a, d);
        a.X = ox; a.Y = oy;
        return odds;
    }

    /// How dangerous is this enemy → how much we want it dead first. VIP/HVT-style high-value
    /// kills and the roles that punish us hardest (snipers, mortars, the medic that undoes our
    /// damage, elites/bosses, cover-stripping sappers) get a priority premium.
    float PriorityWeight(Unit e)
    {
        switch (e.Cls)
        {
            case "ELITE":     return 30f;   // boss / mid-boss: ends the mission, hits hard
            case "WARLORD":   return 32f;
            case "BOMBARD":   return 24f;   // SIEGE artillery: silence it to cancel the telegraphed strike
            case "SNIPER":    return 22f;   // long-range chip from safety
            case "MORTAR":    return 22f;   // back-line AoE we can't easily reach
            case "MEDIC":     return 20f;   // undoes our damage — kill it to stop the heals
            case "SAPPER":    return 14f;   // strips our cover
            case "BERSERKER": return 14f;   // rushes us; better dead before it arrives
            case "HUNTER":    return 13f;   // flanker
            case "DRONE":     return 11f;   // ignores cover; usually fragile, finish it
            case "TURRET":    return 8f;    // immobile but free overwatch
            default:          return 4f;    // grunt / scout / shield
        }
    }

    /// Expected value of a shot described by `odds` against `target`. Roughly
    /// hitChance × expectedDamage, with big bonuses for a finishing blow and a crit-prone
    /// flank/exposed shot, plus the target's threat priority. Used to rank both WHICH foe
    /// to shoot and WHERE to stand to shoot it.
    float ShotValue(ShotOdds odds, Unit target)
    {
        float hit = odds.HitChance / 100f;
        // expected damage of a connecting shot: average dmg, lifted by the crit chance
        // (a crit deals ~1.5×+1). Grazes (the miss-by-≤15 band) add a little guaranteed chip.
        float avgDmg = (odds.DmgMin + odds.DmgMax) * 0.5f;
        float critDmg = avgDmg * 1.5f + 1f;
        float pc = odds.CritChance / 100f;
        float expConnect = avgDmg * (1f - pc) + critDmg * pc;
        // a rough "partial-hit tail" nudge: shots that miss by <= GrazeBand still chip for
        // DmgMin. GrazeBand is a margin in aim-points, not a true probability, so this is a
        // small heuristic bonus (ranking-only), NOT a precise EV term — fine for tie-breaking.
        float grazeChip = Combat.GrazeBand / 100f * odds.DmgMin;
        float ev = hit * expConnect + grazeChip;

        // finisher: if a connecting hit very likely kills, that's worth far more than raw EV
        // (removing a gun from the board). Scale by how reliably we'd land it.
        if (target.Hp <= odds.DmgMin) ev += 14f * hit;               // even a min-roll kills
        else if (target.Hp <= avgDmg) ev += 9f * hit;                // an average roll kills
        else if (target.Hp <= odds.DmgMax) ev += 4f * hit;           // a good roll kills

        if (odds.Flanked) ev += 5f;                                   // flank → reliable crit
        else if (odds.CoverLevel == 0) ev += 2f;                     // exposed
        ev += PriorityWeight(target) * hit * 0.30f;                  // kill the dangerous ones first
        return ev;
    }

    /// Best targetable enemy from tile (ax,ay) and the value of that shot. Considers every
    /// living foe in range+LoS from there. Returns (null, 0) if no shot exists from the tile.
    (Unit tgt, float val) BestShotFrom(Unit u, int ax, int ay)
    {
        if (u.Ammo <= 0) return (null, 0f);
        Unit best = null; float bestVal = 0f;
        int ox = u.X, oy = u.Y; u.X = ax; u.Y = ay;
        bool can(Unit e)                                  // CanTarget evaluated from (ax,ay)
        {
            if (e == null || !e.Alive || (e == Vip && CaptiveLocked)) return false;
            if (Util.TileDist(ax, ay, e.X, e.Y) > u.Weapon.MaxRange) return false;
            bool commanding = Grid.HeightAt(ax, ay) - Grid.HeightAt(e.X, e.Y) >= 2;
            return Grid.HasLineOfSight(ax, ay, e.X, e.Y, commanding);
        }
        foreach (var e in Enemies)
        {
            if (!can(e)) continue;
            var odds = Combat.ComputeOdds(Grid, u, e);
            float v = ShotValue(odds, e);
            if (v > bestVal) { bestVal = v; best = e; }
        }
        u.X = ox; u.Y = oy;
        return (best, bestVal);
    }

    /// The 2nd-highest-value targetable foe from the soldier's current tile, or null if it has
    /// fewer than two shots. Used ONLY by the sloppy policy to model a human mis-prioritisation
    /// (it's a worse-but-legal shot, so the turn still progresses). Read-only.
    Unit SecondBestTarget(Unit u)
    {
        if (u.Ammo <= 0) return null;
        Unit best = null, second = null; float bv = float.NegativeInfinity, sv = float.NegativeInfinity;
        foreach (var e in Enemies)
        {
            if (!e.Alive || (e == Vip && CaptiveLocked)) continue;
            if (Util.TileDist(u.X, u.Y, e.X, e.Y) > u.Weapon.MaxRange) continue;
            bool commanding = Grid.HeightAt(u.X, u.Y) - Grid.HeightAt(e.X, e.Y) >= 2;
            if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y, commanding)) continue;
            float v = ShotValue(Combat.ComputeOdds(Grid, u, e), e);
            if (v > bv) { sv = bv; second = best; bv = v; best = e; }
            else if (v > sv) { sv = v; second = e; }
        }
        return second;
    }

    /// Fire the best expected-value shot the soldier can take from where it stands — but
    /// only if that shot is worth taking (a desperate 3% poke that ends the turn is usually
    /// worse than repositioning). Returns true if it shot.
    /// Autopilot: shoot an explosive barrel that would catch 2+ enemies (and no friendly) in its
    /// blast — a guaranteed multi-hit AoE worth more than a single aimed shot. Exercises the
    /// hazard system in the flywheel. Returns true if it fired.
    bool TryShootBarrel(Unit u)
    {
        if (u.Ammo <= 0 || !u.CanAct) return false;
        int bx = -1, by = -1, best = 0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                if (!Grid.IsBarrel(x, y)) continue;
                if (!CanShootBarrel(u, x, y)) continue;
                int foes = Enemies.Count(e => e.Alive && Util.ChebyDist(e.X, e.Y, x, y) <= BarrelRadius);
                bool allyHit = Players.Any(p => p.Alive && Util.ChebyDist(p.X, p.Y, x, y) <= BarrelRadius);
                if (allyHit || foes < 2) continue;
                if (foes > best) { best = foes; bx = x; by = y; }
            }
        if (best < 2) return false;
        if (SquadConcealed) BreakConcealment(u);
        u.Ammo--; u.ActionsLeft = 0;
        u.Steady = false; u.FiredFromConcealment = false;
        SetBarrelCredit(u);
        Enqueue(new BarrelShotAnim(u, bx, by), Team.Player);
        Stats.RecordAction("BARREL");   // W2 verb telemetry (this issuer bypasses IssueShootBarrel)
        return true;
    }

    bool TakeBestShot(Unit u)
    {
        if (u.Ammo <= 0) return false;
        if (TryShootBarrel(u)) return true;     // a 2+-enemy barrel beats any single shot
        var (tgt, val) = BestShotFrom(u, u.X, u.Y);
        // SLOPPY: ~15% of the time aim at the 2nd-best target instead (a human mis-prioritisation)
        // — still a real shot, just a worse pick, so the run is harder but never stalls.
        if (Slip(15)) { var alt = SecondBestTarget(u); if (alt != null) tgt = alt; }
        if (tgt == null) return false;
        var odds = Combat.ComputeOdds(Grid, u, tgt);
        // worth firing? a hit chance floor OR a likely finisher (a near-certain kill of a
        // low-HP foe is worth a poor-percentage shot). Otherwise prefer to reposition.
        bool finisher = tgt.Hp <= odds.DmgMax && odds.HitChance >= 35;
        bool decent   = odds.HitChance >= 45;
        // if the soldier has BOTH actions, a weak shot is fine via SNAP (keeps acting); a
        // turn-ending aimed shot should clear a higher bar. Either way, take a real chance.
        // TEMPO: firing no longer ends the turn, so a marginal shot is cheap (the soldier keeps an
        // action to reposition). With two actions in hand, take any real chance; with one, hold a
        // higher bar so we don't waste the soldier's only action on a coin-flip.
        bool twoActions = u.ActionsLeft >= 2;
        if (!finisher && !decent && !(twoActions && odds.HitChance >= 30)) return false;
        AutoShootSmart(u, tgt);
        return true;
    }

    /// True if the soldier has a high-confidence shot from where it stands (used by the
    /// objective routines to decide "is a kill worth pausing the advance for?").
    bool HasStrongShot(Unit u)
    {
        if (u.Ammo <= 0) return false;
        var (tgt, _) = BestShotFrom(u, u.X, u.Y);
        if (tgt == null) return false;
        var odds = Combat.ComputeOdds(Grid, u, tgt);
        return odds.HitChance >= 60 || (tgt.Hp <= odds.DmgMax && odds.HitChance >= 50);
    }

    /// Fire at `tgt`. TEMPO: the aimed shot is now always 1 action and never ends the turn, so
    /// there's no SNAP/AIMED decision to make — fire at full aim and let the soldier keep its
    /// second action for repositioning. The "duck vs double-tap" bet is played the NEXT SmartStep:
    /// SmartCombatStep calls SmartRetreatAfterShot before its rushed-2nd-shot path (see below).
    void AutoShootSmart(Unit u, Unit tgt)
    {
        IssueShoot(tgt);
    }

    /// HORIZON W1 — the post-shot tempo bet. Called once the unit has already FIRED this turn and
    /// hasn't moved since (so it's EXPOSED BY FIRE), with an action still in hand. Weighs ducking to
    /// a safer tile against a rushed 2nd shot: if a genuine FINISHER is available from here, keep the
    /// shot (return false, let the cascade take the kill); if we're already safe, or no tile is
    /// meaningfully safer, don't move (return false, let the rushed shot / other actions run). Only
    /// when ducking clearly reduces exposure do we issue a real move and return true. Preconditions
    /// (FiredThisTurn && !MovedAfterFire && ActionsLeft > 0) are checked by the caller.
    /// CRITICAL: never return true without issuing a real move (a stall would risk a TIMEOUT).
    bool SmartRetreatAfterShot(Unit u)
    {
        if (MoveCost == null) return false;

        // Don't duck away from a near-certain finishing 2nd shot from the current tile.
        var (fTgt, _) = BestShotFrom(u, u.X, u.Y);
        if (fTgt != null)
        {
            var fOdds = Combat.ComputeOdds(Grid, u, fTgt);
            if (fTgt.Hp <= fOdds.DmgMax && fOdds.HitChance >= 50) return false;   // let the cascade take the kill
        }

        float curExp = TileExposure(u, u.X, u.Y);
        if (curExp < 1.5f) return false;                    // already safe — no reason to duck

        // Mirror the SafetyAt scoring from CountMeaningfulChoices: minimize exposure, then prefer
        // more cover vs the nearest alive foe, then higher ground (as tie-breaks folded into a score).
        var nearest = AliveEnemies().OrderBy(en => Util.TileDist(u.X, u.Y, en.X, en.Y)).FirstOrDefault();
        float Safety(int x, int y)
        {
            float s = -TileExposure(u, x, y);
            if (nearest != null) s += Grid.GetCover(x, y, nearest.X, nearest.Y).Level * 8f;
            s += Grid.HeightAt(x, y) * 5f;
            return s;
        }

        var pcost = Grid.CostMap(u.X, u.Y, (x, y) => IsOccupiedByOther(x, y, u), out _, u.MoveBudget * 2);
        int bx = -1, by = -1; float bestSafety = Safety(u.X, u.Y); float bestExp = curExp;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = pcost[x, y];
                if (c <= 0 || c > u.MoveBudget) continue;    // 1-action-reachable steps only
                float s = Safety(x, y);
                if (s > bestSafety) { bestSafety = s; bx = x; by = y; bestExp = TileExposure(u, x, y); }
            }

        // only duck if the chosen tile meaningfully reduces exposure (else a rushed shot is better).
        if (bx >= 0 && bestExp <= curExp - 1.5f) { IssueMove(bx, by); return true; }
        return false;
    }

    /// Prep a class ability when it improves THIS soldier's turn. Deliberate (never random):
    ///   - PATCH (corpsman): heal the most-wounded adjacent ally if it's meaningfully hurt;
    ///   - STEADY (sharpshooter): brace before a real shot to sharpen it;
    ///   - SUPPRESS (gunner): pin a dangerous foe we can't cleanly kill;
    ///   - RUN&GUN (assault) / BLITZ (ranger): free tempo stances — take them when they help.
    /// Returns true if it spent the turn on the ability (Steady/Suppress/Heal cost an action;
    /// RunGun/Blitz are free, so they DON'T return true — the soldier acts with them this step).
    bool PrepAbility(Unit u) => PrepAbilityFor(u, null);

    bool PrepAbilityFor(Unit u, Unit forcedTarget)
    {
        if (!CanAbility(u)) return false;
        switch (u.Ability)
        {
            case AbilityKind.Heal:
            {
                // FUL-5: patch when an adjacent ally is genuinely hurt (missing >=3 — was >=4,
                // which wasted the corpsman's whole kit on the 4-6 maxHP roster where "missing 4"
                // is often one hit from dead) — MostWoundedAdjacentAlly already gates on Hp<MaxHp.
                var ally = MostWoundedAdjacentAlly(u);
                if (ally != null && ally.MaxHp - ally.Hp >= 3) { DoAbility(); return true; }
                return false;
            }
            case AbilityKind.Steady:
            {
                // brace only if there's a real shot to sharpen and we can still fire after
                // (Steady costs one action; need >=2 so a shot remains). Worth it for a shot
                // that isn't already near-certain.
                if (u.ActionsLeft < 2 || u.Ammo <= 0) return false;
                var tgt = forcedTarget != null && CanTarget(u, forcedTarget) ? forcedTarget : FirstTargetFor(u);
                if (tgt == null) return false;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                if (odds.HitChance >= 40 && odds.HitChance <= 90) { DoAbility(); return true; }
                return false;
            }
            case AbilityKind.Suppress:
            {
                // pin a foe we can see — best used on a dangerous attacker we can't reliably
                // kill outright (it slashes its aim and trains overwatch on it). Skip if we
                // already have a clean kill shot (just take the kill instead).
                var tgt = FirstTargetFor(u);
                if (tgt == null) return false;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                bool cleanKill = tgt.Hp <= odds.DmgMax && odds.HitChance >= 55;
                if (cleanKill) return false;                       // prefer the kill
                if (PriorityWeight(tgt) >= 14f || odds.HitChance < 45) { DoAbility(); return true; }
                return false;
            }
            case AbilityKind.RunGun:
            {
                // free stance: only worth it if firing this turn (so the shot doesn't end the
                // turn, letting the soldier move+shoot or shoot twice). Take it before a shot.
                if (u.Ammo > 0 && FirstTargetFor(u) != null) { DoAbility(); return false; }  // free → act with it
                return false;
            }
            case AbilityKind.Blitz:
            {
                // free stance: cheap movement. Take it when we have no shot and need to close
                // distance toward a foe/objective this turn (so the move costs one less action).
                if (FirstTargetFor(u) == null && AliveEnemies().Count > 0) { DoAbility(); return false; }
                return false;
            }
            case AbilityKind.Mark:
            {
                // designate a high-value visible foe so the whole squad shoots it better this round.
                // Costs an action; only worth it when we can still fire after AND there's a
                // worthwhile target nobody's marked. Don't mark a foe we can already cleanly kill.
                if (u.ActionsLeft < 2) return false;     // keep an action to actually shoot
                var tgt = forcedTarget != null && MarkTargetOk(u, forcedTarget) ? forcedTarget : BestMarkTarget(u);
                if (tgt == null) return false;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                if (tgt.Hp <= odds.DmgMax && odds.HitChance >= 60) return false;  // just kill it
                IssueMark(u, tgt);
                return false;   // didn't end the turn — fall through and act (shoot) with the action left
            }
            case AbilityKind.Grapple:
            {
                // yank a foe out of cover so the squad can hit it. Only when there's a covered foe
                // in reach AND we can act after (the grapple costs an action but not the turn).
                if (u.ActionsLeft < 2 || u.ShovedThisTurn) return false;
                var tgt = BestGrappleTarget(u);
                if (tgt == null) return false;
                if (Grid.GetCover(tgt.X, tgt.Y, u.X, u.Y).Level <= 0) return false;  // only worth it vs a covered foe
                IssueGrapple(u, tgt);
                // W9: TRUE = "this step is done". The grapple queues a ShoveAnim that has not run yet;
                // acting again now decides from a board the anim is about to change (and is how a
                // grappler ended up firing a shot after its own grapple downed it). The soldier keeps
                // its remaining action and uses it on the next SmartStep, once the queue has drained.
                return true;
            }
            case AbilityKind.Slipstream:
            {
                // RANGER free reposition: take it when we have no shot but need to close on a foe/objective —
                // a 0-action, overwatch-immune move. Like Blitz it's free, so DON'T return true (act with it).
                if (FirstTargetFor(u) == null && AliveEnemies().Count > 0) { DoAbility(); return false; }
                return false;
            }
            case AbilityKind.Pin:
            {
                // GUNNER area denial: lay suppressing fire on a CLUSTER of foes (2+ in the 3x3) we can't
                // cleanly kill — it ends the turn (the full burst), so prefer it over a single weak shot.
                var tgt = BestPinTarget(u);
                if (tgt == null) return false;
                int cluster = 0;
                foreach (var o in Enemies)
                    if (o.Alive && o.Team == Team.Enemy && Util.ChebyDist(tgt.X, tgt.Y, o.X, o.Y) <= 1) cluster++;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                bool cleanKill = tgt.Hp <= odds.DmgMax && odds.HitChance >= 55;
                if (cleanKill) return false;                  // just take the kill
                if (cluster >= 2 || odds.HitChance < 45) { IssuePin(u, tgt); return true; }  // burst denies the zone
                return false;
            }
        }
        return false;
    }

    /// FUL-5 — the PATCH approach step: if this soldier is a corpsman with the kit ready and a
    /// non-VIP ally missing >=3 HP sits within Chebyshev 2-3 (but NOT already adjacent — that
    /// case is PrepAbility's), move onto the cheapest reachable tile adjacent to that ally. The
    /// heal itself fires on a later pass via PrepAbility (adjacency then holds). Cd-gated so the
    /// corpsman never shadows a soldier it can't actually treat yet. Returns true iff it moved.
    /// (R4: radius 2 -> 2-3 — the R3 reading showed the greedy spread holds soldiers 3+ apart,
    /// so the exact-2 window fired only ~4/20 campaigns; 4+ stays out of the medic's remit.)
    bool TryMoveToPatch(Unit u)
    {
        // FUL-7 generalization: a Cd-ready CORPSMAN closes on hurt-or-downed allies (heal or
        // revive next pass); EVERY OTHER soldier — including a Cd'd corpsman — closes on a
        // downed, un-stabilized ally to STABILIZE (their only rescue verb). Same Cheby-2-3
        // window, same bounded approach, so a founding squad with no corpsman still answers.
        if (u.ActionsLeft <= 0 || u.IsVip || MoveCost == null) return false;
        bool corpsman = u.Ability == AbilityKind.Heal && u.AbilityCd == 0;
        Unit tgt = null; float worst = 1f;
        foreach (var a in AlivePlayers())
        {
            if (a == u || a.IsVip || a.MaxHp <= 0) continue;
            bool eligible = a.Downed
                ? (corpsman || (!a.Stabilized && AutoShouldStabilize(a)))
                : (corpsman && a.MaxHp - a.Hp >= 3);
            if (!eligible) continue;
            int d = Util.ChebyDist(u.X, u.Y, a.X, a.Y);
            if (d < 2 || d > 3) continue;               // adjacent = act now (SmartStep's rescue block / PrepAbility); 4+ = not our call
            float frac = (float)a.Hp / a.MaxHp;         // downed = 0 -> outranks the merely hurt
            if (tgt == null || frac < worst) { tgt = a; worst = frac; }
        }
        if (tgt == null) return false;
        int bx = -1, by = -1, bestC = int.MaxValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = tgt.X + dx, ny = tgt.Y + dy;
                if (!Grid.InBounds(nx, ny) || !Grid.IsFloor(nx, ny) || IsOccupiedByOther(nx, ny, u)) continue;
                int c = MoveCost[nx, ny];
                if (c <= 0 || c > u.MoveBudget * u.ActionsLeft) continue;   // must be affordable this turn
                if (c < bestC) { bestC = c; bx = nx; by = ny; }
            }
        if (bx < 0) return false;
        IssueMove(bx, by);
        return true;
    }

    /// FUL-7 — the bot's stabilize policy. Always yes EXCEPT the one freeze that can never
    /// resolve: on EVAC, with no live corpsman in the squad (no revive possible) and the body
    /// outside/away from the zone, a frozen timer blocks the all-in-zone win forever — the bot
    /// has no drag-chain carry in v1. Let the timer run there (bounded; the honest bot gap).
    /// Zone-adjacent bodies stay stabilizable: the in-zone EXTRACT pull can haul them aboard.
    bool AutoShouldStabilize(Unit body)
    {
        if (Objective != Objective.Evac) return true;
        if (Players.Any(p => p.Alive && !p.Downed && !p.IsVip && p.Ability == AbilityKind.Heal)) return true;
        return DistToEvac(body.X, body.Y) <= 1;
    }

    /// Grenade decision: lob at the cluster of enemies that catches the most foes (≥2),
    /// or flush a single well-covered/high-priority target our gun can't crack.
    /// Never catches an ally. Mirrors the enemy grenade AI's fairness (LoS-gated by CanGrenade
    /// through IssueGrenade's range check + our own LoS test). Returns true if it threw.
    /// FUL-5 `preShot`: the step-2a call, BEFORE the gun — accepts ONLY a covered 2+ cluster
    /// (the case where the frag strictly beats shooting); the post-shot call keeps the wider
    /// single-target acceptances.
    bool SmartGrenade(Unit u, bool preShot = false)
    {
        if (u.Grenades <= 0) return false;
        int bx = -1, by = -1, bestHits = 0; bool bestCovered = false; float bestPrio = 0f;
        foreach (var e in AliveEnemies())
        {
            if (!CanGrenade(u, e.X, e.Y)) continue;                 // in range + LoS from here
            if (!NoAllyInBlast(e.X, e.Y)) continue;                 // never frag our own
            int hits = 0;
            foreach (var q in AliveEnemies()) if (Util.ChebyDist(e.X, e.Y, q.X, q.Y) <= GrenadeAnim.Radius) hits++;
            // is the aim foe well-covered from us (so our bullets are weak)?
            var odds = SmartOdds(u, u.X, u.Y, e);
            bool covered = odds.CoverLevel >= 1 || odds.HitChance < 45;
            float prio = PriorityWeight(e);
            if (preShot && (hits < 2 || !covered)) continue;        // FUL-5: pre-shot wants covered clusters only
            if (hits > bestHits || (hits == bestHits && prio > bestPrio))
            { bestHits = hits; bx = e.X; by = e.Y; bestCovered = covered; bestPrio = prio; }
        }
        if (bx < 0) return false;
        if (preShot) { IssueGrenade(bx, by); return true; }         // candidates were pre-filtered above
        // throw when it catches 2+, OR a single target that's well-covered or high-priority
        // (a frag ignores cover) — i.e. when the grenade beats what our gun would do.
        if (bestHits >= 2 || (bestHits == 1 && (bestCovered || bestPrio >= 20f)))
        { IssueGrenade(bx, by); return true; }
        return false;
    }

    /// True if (tx,ty) is in grenade range AND the soldier has line of sight to it (no
    /// blind lobbing over high cover / through smoke — matches the perfect-info contract).
    bool CanGrenade(Unit u, int tx, int ty)
        => Util.TileDist(u.X, u.Y, tx, ty) <= GrenadeRange && Grid.HasLineOfSight(u.X, u.Y, tx, ty);

    /// Per-tile exposure (mirrors ComputeThreat, but always available and not gated on the
    /// player pref): how many live, active, armed enemies could fire on (x,y) with NO cover
    /// for the mover. Weighted by the shooter's threat priority, so standing exposed to a
    /// sniper hurts the score more than exposure to a grunt. This is the threat term the
    /// brief asks us to SUBTRACT from destination tiles.
    float TileExposure(Unit mover, int x, int y)
    {
        float threat = 0f;
        if (Grid.IsFire(x, y)) threat += 20f;   // never voluntarily end a move standing in fire (hazards)
        // C4 / MAGMA: the same price the enemy planner pays for ending on a thermal vent, on the
        // same scale as this function's other hazard terms — so the balance bot does not walk the
        // squad into the fissure and hand the wave a win-rate drop that is a BOT defect, not a
        // design consequence. Smaller than fire's 20 because a vent is a toll, not a trap.
        if (Grid.IsVent(x, y)) threat += 12f;
        if (InSiegeZone(x, y)) threat += 30f;   // a charged SIEGE strike WILL land here -> vacate (cover-ignoring)
        // FUL-8 PIKEMAN: a live enemy BRACE lane costs a TURN (stagger), not a life — weighted between
        // an exposed gun (~6-10) and the siege zone's 30, so the bot paths around it, not through it.
        // Review fix: an overwatch-immune mover (Sprinter/Outrunner) can never be reacted to — no
        // phantom detour for the one soldier the lane can't touch.
        if (!Combat.IgnoresOverwatch(mover) && InEnemyBraceLane(x, y)) threat += 18f;
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Active || e.Ammo <= 0) continue;
            if (Util.TileDist(x, y, e.X, e.Y) > e.Weapon.MaxRange) continue;
            if (!Grid.HasLineOfSight(e.X, e.Y, x, y)) continue;
            // cover for the MOVER standing at (x,y) against this shooter
            if (Grid.GetCover(x, y, e.X, e.Y).Level == 0)
                threat += 6f + PriorityWeight(e) * 0.5f;            // exposed to this gun
        }
        return threat;
    }

    /// Score a prospective destination tile for `u`, Ai.Plan-style but from the PLAYER's
    /// perspective: reward a good shot available from there, cover, high ground, a flank on
    /// the nearest foe; subtract exposure (threat) and movement cost. `advanceTarget`, when
    /// given, adds a mild pull toward it (objective/foe) so positioning still makes progress.
    float ScoreDestTile(Unit u, int x, int y, int actionsToReach, Unit nearest, (int x, int y)? advanceTarget)
    {
        float score = 0f;

        // a shot from here is the biggest prize (only if we'd keep an action to fire it).
        if (actionsToReach <= 1)
        {
            var (tgt, val) = BestShotFrom(u, x, y);
            if (tgt != null) score += 60f + val * 2.2f;
        }

        // terrain: cover + height vs the nearest foe (use it as the reference angle).
        if (nearest != null)
        {
            var cov = Grid.GetCover(x, y, nearest.X, nearest.Y);
            score += cov.Level * 16f;
            if (cov.Flanked) score -= 18f;                         // our own cover useless from here
        }
        score += Grid.HeightAt(x, y) * 12f;                        // seize high ground

        // exposure: avoid tiles a live enemy can shoot with no cover for us (the threat term).
        score -= TileExposure(u, x, y);

        score -= actionsToReach * 5f;                              // prefer cheaper moves

        // mild pull toward the advance target so repositioning still closes the gap.
        if (advanceTarget != null)
            score -= Util.ChebyDist(x, y, advanceTarget.Value.x, advanceTarget.Value.y) * 1.4f;

        score += Util.RandRange(0f, 2f);                           // tie-break jitter
        return score;
    }

    /// Reposition toward the best firing tile on the nearest foe: an Ai.Plan-style sweep of
    /// reachable tiles scored by ScoreDestTile (cover/height/flank/shot − exposure − cost,
    /// pulled toward the foe). Moves there if it beats standing still. This is the core
    /// "advance behind cover and set up flanks" behaviour. Returns true if it moved.
    bool SmartApproach(Unit u)
    {
        var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        if (foe == null) return false;
        return MoveToBestTile(u, foe, (foe.X, foe.Y));
    }

    /// Like SmartApproach but with no advance pull — purely shuffle into better cover/safety
    /// near where we already are (used when holding a position, e.g. DEFEND / a held zone).
    bool SmartReposition(Unit u)
    {
        var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        return MoveToBestTile(u, foe, null);
    }

    /// Move `u` to the best-scoring reachable tile (vs the value of staying put). `nearest`
    /// is the reference foe for cover/flank scoring; `advance`, if set, pulls toward a goal.
    /// Falls back to the distance-only TryMoveTowardTile if scoring finds nothing better, so
    /// progress toward the goal is still guaranteed. Returns true if it issued a move.
    /// W2: under the SLOPPY policy the full scored candidate list is kept so SloppyDest can
    /// substitute a bounded positional mistake for the optimum — positioning is the dominant
    /// human skill axis, and until now the sloppy bot only mis-picked TARGETS, never TILES.
    bool MoveToBestTile(Unit u, Unit nearest, (int x, int y)? advance)
    {
        if (MoveCost == null) return false;
        // score of staying at the current tile (it's always "reachable" at cost 0).
        float stayScore = ScoreDestTile(u, u.X, u.Y, 0, nearest, advance);
        int bx = -1, by = -1; float bestScore = stayScore;
        var cands = SmartSloppy ? new List<(int x, int y, float s)>() : null;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y];
                if (c <= 0) continue;                              // unreachable / current tile
                int need = c <= u.MoveBudget ? 1 : 2;
                int cost = u.Blitz ? Math.Max(0, need - 1) : need;
                if (cost > u.ActionsLeft) continue;                // can't afford it
                float s = ScoreDestTile(u, x, y, need, nearest, advance);
                cands?.Add((x, y, s));
                if (s > bestScore) { bestScore = s; bx = x; by = y; }
            }
        if (bx >= 0)
        {
            (bx, by) = SloppyDest(nearest, bx, by, stayScore, bestScore, cands);
            IssueMove(bx, by);
            return true;
        }
        // nothing scored better than standing still: if we have a goal, still close on it so
        // the match never stalls (guaranteed progress). Otherwise stay put (caller hunkers).
        if (advance != null) return TryMoveTowardTile(u, advance.Value.x, advance.Value.y);
        return false;
    }

    /// W2 — POSITIONAL SLOPPINESS (the sloppy policy's tile-level error model). Given the best
    /// destination MoveToBestTile found, sometimes swap in a human-shaped mistake:
    ///   (a) ~15%: settle for a MEDIOCRE tile — drawn from the bottom half of the candidates
    ///       that still score >= the stay-score (and within a fixed margin of the best), so a
    ///       slip reads as lazy positioning, never a drunk teleport into the open;
    ///   (b) else ~10%: OVEREXTEND — take a tile one step past the best pick toward the nearest
    ///       enemy (the "greedy push" error that walks into pod-wake / flank angles).
    /// Every substituted tile comes from the affordability-filtered candidate list, so the move
    /// is always legal and the progress invariant holds. No-op (returns the best tile) for the
    /// greedy policy — Slip() is hard-gated on SmartSloppy and cands is null there.
    (int x, int y) SloppyDest(Unit nearest, int bx, int by, float stayScore, float bestScore,
                              List<(int x, int y, float s)> cands)
    {
        if (cands == null || cands.Count < 2) return (bx, by);
        if (Slip(15))
        {
            float floor = Math.Max(stayScore, bestScore - 40f);   // still-reasonable band
            var ok = cands.Where(c => c.s >= floor && !(c.x == bx && c.y == by))
                          .OrderByDescending(c => c.s).ToList();
            if (ok.Count > 0)
            {
                var bottom = ok.Skip(ok.Count / 2).ToList();      // the mediocre half
                if (bottom.Count == 0) bottom = ok;               // (ok.Count==1 -> Skip(0) keeps it; defensive)
                var pick = bottom[SlipPick(bottom.Count)];
                return (pick.x, pick.y);
            }
            return (bx, by);
        }
        if (nearest != null && Slip(10))
        {
            int curDist = Util.ChebyDist(bx, by, nearest.X, nearest.Y);
            int ox = -1, oy = -1; float overBest = float.NegativeInfinity;
            foreach (var c in cands)
            {
                if (Util.ChebyDist(c.x, c.y, bx, by) != 1) continue;                    // one tile past the pick
                if (Util.ChebyDist(c.x, c.y, nearest.X, nearest.Y) >= curDist) continue; // must close on the foe
                if (c.s > overBest) { overBest = c.s; ox = c.x; oy = c.y; }
            }
            if (ox >= 0) return (ox, oy);
        }
        return (bx, by);
    }

    /// SIEGE flee: move `u` to the best reachable tile NOT inside any live strike zone (cover-aware
    /// via ScoreDestTile, which now penalizes zone tiles by +30, so this picks a safe-AND-good spot).
    /// Returns false only if no out-of-zone tile is reachable (very rare given squad mobility vs a 3x3)
    /// — the caller then falls through to normal routing, which always ends in DoHunker (no TIMEOUT).
    bool SmartFleeSiege(Unit u)
    {
        if (MoveCost == null) return false;
        var nearest = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        (int x, int y)? adv = nearest != null ? (nearest.X, nearest.Y) : ((int, int)?)null;
        int bx = -1, by = -1; float best = float.NegativeInfinity;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y]; if (c <= 0) continue;
                int need = c <= u.MoveBudget ? 1 : 2;
                int cost = u.Blitz ? Math.Max(0, need - 1) : need;
                if (cost > u.ActionsLeft) continue;
                if (InSiegeZone(x, y)) continue;                  // must leave the zone
                float s = ScoreDestTile(u, x, y, need, nearest, adv);
                if (s > best) { best = s; bx = x; by = y; }
            }
        if (bx >= 0) { IssueMove(bx, by); return true; }
        return false;
    }

    /// Path toward (gx,gy) but prefer covered/safe stepping tiles when the move can reach
    /// the goal area: try the cover-aware sweep first (pulled toward the goal), then fall
    /// back to the plain distance-only step so progress is always guaranteed.
    bool SmartMoveToward(Unit u, int gx, int gy)
    {
        var nearest = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        // only bother with cover-aware routing when there's a live threat to avoid; with no
        // active enemies, march straight (faster to the objective, and TileExposure is 0).
        if (nearest != null && Enemies.Any(e => e.Alive && e.Active))
        {
            if (MoveToBestTile(u, nearest, (gx, gy))) return true;
        }
        return TryMoveTowardTile(u, gx, gy);
    }

    /// VIP/asset advance toward (gx,gy). The asset dies in one or two hits and the enemy AI
    /// hunts it, so this is SURVIVAL-FIRST: only bound forward into a tile that's genuinely
    /// SAFE (no live enemy can shoot it there — exposure 0 — or it ends in cover). Among safe
    /// Hold overwatch when it's the right call: the soldier has ammo + an action, isn't
    /// disoriented, and a live enemy is near enough to plausibly walk into the lane this
    /// enemy turn (so we don't waste overwatch staring at an empty board). Returns true if set.
    /// FUL-5: the committed-charger archetypes (Ai.cs never breaks these off) — the pods brace
    /// is FOR: they close to point-blank and swing, so a stagger denies a whole attack.
    static bool IsRusherCls(string c)
        => c == "BERSERKER" || c == "HOUND" || c == "STRIKER" || c == "BRUISER";

    /// FUL-5 — the combat-brain brace probe (step 5a). True iff an ACTIVE committed charger is
    /// close enough to reach this soldier on the coming enemy turn (Mobility + 2: one move plus
    /// the point-blank swing) AND a lethal reaction couldn't remove it (too durable for one
    /// reaction shot — BERSERKER 12 / BRUISER 9 vs player DmgMax 4-7 — or a 2+ pack where one
    /// kill doesn't stop the charge). Then the stagger's action-denial beats both the approach
    /// (walking into the charge) and the lethal watch (a chip that doesn't stop the swing).
    /// FUL-5 — the condition HALF of the brace probe, callable as a pure test (the step-1b duck
    /// veto needs it without issuing). See TryBraceRushers for the rationale.
    bool RusherBraceWorthy(Unit u)
    {
        if (u.Ammo <= 0 || u.ActionsLeft <= 0 || u.HasStatus(StatusKind.Disoriented)) return false;
        var inbound = Enemies.Where(e => e.Alive && e.Active && IsRusherCls(e.Cls)
            && Util.TileDist(u.X, u.Y, e.X, e.Y) <= e.Mobility + 2).ToList();
        if (inbound.Count == 0) return false;
        return inbound.Count >= 2 || inbound.Any(e => e.Hp > u.Weapon.DmgMax);
    }

    bool TryBraceRushers(Unit u)
    {
        if (!RusherBraceWorthy(u)) return false;
        Selected = u;   // DoBrace acts on Selected (set defensively, TrySmartItem precedent)
        DoBrace();
        return true;
    }

    bool HoldOverwatch(Unit u)
    {
        if (u.Ammo <= 0 || u.ActionsLeft <= 0 || u.HasStatus(StatusKind.Disoriented)) return false;
        // a foe that's active and within a turn's move + weapon reach is a credible pusher.
        var pushers = Enemies.Where(e => e.Alive && e.Active
            && Util.TileDist(u.X, u.Y, e.X, e.Y) <= e.Weapon.MaxRange + e.Mobility).ToList();
        if (pushers.Count == 0) return false;
        // UNDERTOW W2 probe: BRACE is a LOSING-POSITION tool — denying a pusher's action buys a turn but
        // forgoes damage, so ROUTINE bracing loses the attrition race (it inverts the policy gap). A good
        // player braces only to PROTECT a threatened wounded soldier from a finishing blow it can't
        // prevent by killing the shooter. Gate on exactly that: a durable pusher + a low-HP squadmate in
        // its reach. Otherwise a normal lethal watch. This keeps brace a rare, genuinely-good pick and
        // makes the flywheel exercise it in the comeback situations it's for.
        var pusher = pushers.OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).First();
        bool cantKillOnReaction = pusher.Hp > u.Weapon.DmgMax;
        bool woundedUnderThreat = Players.Any(p => p.Alive && !p.IsVip && p.MaxHp > 0 && p.Hp * 2 <= p.MaxHp
            && pushers.Any(e => Util.TileDist(p.X, p.Y, e.X, e.Y) <= e.Weapon.MaxRange + e.Mobility));
        // FUL-5 HANDS: + the RUSHER arm. A committed charger closing on the squad is brace's
        // textbook case even with everyone healthy: the charger WILL reach us, a lethal watch
        // can't remove it on the reaction (too durable, or there are two-plus of them), and a
        // landed stagger denies its post-move attack outright — denial > a half-damage chip.
        // The cascade already re-tries TakeBestShot every step, so reaching here means no
        // worthwhile shot exists from this tile (the spec's "no >=60% kill shot" is structural).
        var rushers = pushers.Where(e => IsRusherCls(e.Cls)).ToList();
        bool rusherInbound = rushers.Count >= 2 || rushers.Any(e => e.Hp > u.Weapon.DmgMax);
        if ((cantKillOnReaction && woundedUnderThreat) || rusherInbound) { DoBrace(); return true; }
        // W2 FOCUS probe (mirrors the BRACE probe's shape): when EVERY credible pusher approaches
        // down ONE lane — all inside a single 90-degree cone centred on the nearest pusher — the
        // focused watch strictly dominates the wide one (+Combat.FocusOwAim on the reaction, and
        // no other lane exists to leave blind). Multi-lane threats keep the wide watch: a cone
        // there would trade a flank's coverage for aim. This finally exercises the COUNTERPLAY
        // cone verb in the flywheel (FOCUS was invisible to measurement before this wave).
        int coneDx = pusher.X - u.X, coneDy = pusher.Y - u.Y;
        bool oneLane = (coneDx != 0 || coneDy != 0)
            && pushers.All(e => InConeDir(u.X, u.Y, coneDx, coneDy, e.X, e.Y));
        if (oneLane) IssueFocusWatch(u, coneDx, coneDy); else DoOverwatch();
        return true;
    }

    // ---- W9 THE REPAIR: the WITHIN-TURN deadlock guard -------------------------------------
    // AutoStallCheck below runs in StartPlayerTurn, so by construction it can only ever catch a
    // stall that spans TURN BOUNDARIES. An autopilot step that returns WITHOUT spending an action
    // and WITHOUT ending the turn never reaches a boundary at all, so the only thing that ended
    // such a run was the harness frame cap — a RESULT: TIMEOUT the autopilot's own contract calls
    // unreachable. One instance was found and fixed at source (the disoriented DEFEND overwatch,
    // above); this is the STRUCTURAL backstop, so the next one costs a forced turn instead of a
    // phantom pre-merge failure that an agent spends a session chasing.
    // Fires only when literally nothing changed: player turn, empty anim queue, identical
    // action/ammo/HP/position signature for AutoIdleFrames consecutive updates. Any real action
    // moves the signature, so a healthy fight can never trip it. Autoplay-only.
    const int AutoIdleFrames = 240;   // 4 s of a frozen board with nothing animating
    int _autoIdleSig, _autoIdleFrames;
    void AutoIdleGuard()
    {
        if (_anims.Count > 0) { _autoIdleFrames = 0; return; }
        int sig = 17;
        foreach (var p in Players)
            sig = sig * 31 + (p.ActionsLeft * 8191 + p.Ammo * 127 + p.Hp * 7 + p.X * 31 + p.Y
                              + (p.Alive ? 1 : 0) + (p.Downed ? 2 : 0) + p.Statuses.Count * 3);
        foreach (var e in Enemies) sig = sig * 31 + (e.Hp * 7 + e.X * 31 + e.Y + (e.Alive ? 1 : 0));
        if (sig != _autoIdleSig) { _autoIdleSig = sig; _autoIdleFrames = 0; return; }
        if (++_autoIdleFrames < AutoIdleFrames) return;
        _autoIdleFrames = 0;
        // Force the turn to end. StartPlayerTurn then runs AutoStallCheck, whose run-scoped cap
        // (AutoMaxRunTurns) closes the run for good if the deadlock simply repeats — so the only
        // exits left are WIN and LOSE.
        EndPlayerTurn();
    }

    void AutoStallCheck()
    {
        // HARD no-TIMEOUT backstop: a match still going at AutoMaxTurns is effectively stalled (normal
        // matches resolve in ~5-15 turns; Defend caps at 8). Force-lose so the smoke test / balance
        // batch ALWAYS terminates well before the frame cap — never a RESULT: TIMEOUT. Only ever fires
        // in autoplay (this method is autoplay-only); real play is unaffected.
        // W9 THE REPAIR: the RUN-scoped arm. `_turnCount` is reset to 1 by SetupMission at every
        // mission start AND at the mid-mission checkpoint redeploy, so the per-mission cap below could
        // never bound a CAMPAIGN — which is the thing the harness's frame budget bounds. RunTurns is
        // never reset by SetupMission, so this arm is the one that makes the comment above true.
        if (_turnCount > AutoMaxTurns || RunTurns > AutoMaxRunTurns)
        {
            // PROGRAM HORIZON W2: in LAST STAND the "turn cap" just ends the horde run cleanly at the
            // waves survived so far (route through EndEndless, not the campaign LoseRun).
            if (Mode == GameMode.Endless) { EndEndless(); return; }
            LoseRun("STALEMATE", $"Autopilot exceeded the turn cap on mission {_run.Mission}.");
            return;
        }

        int sig = AliveEnemies().Count * 1000
                + AliveEnemies().Count(e => e.Active) * 10
                + HackProgress
                + AlivePlayers().Count(p => EvacZone.Contains((p.X, p.Y)));
        if (sig != _autoSig) { _autoSig = sig; _autoStall = 0; return; }
        if (++_autoStall < 10) return;
        _autoStall = 0;
        if (SquadConcealed) BreakConcealment();   // 4.4: a stalled autopilot reveals itself
        var dormant = Enemies.Where(e => e.Alive && !e.Active).ToList();
        if (dormant.Count > 0) ActivatePod(dormant[0].PodId);
    }

    void AutoStep()
    {
        if (_anims.Count > 0 || Phase != Phase.PlayerTurn) return;
        TryFreeCaptive();                       // free a captive a soldier is already standing next to
        var u = Players.FirstOrDefault(p => p.CanAct);
        if (u == null) { EndPlayerTurn(); return; }
        Selected = u;
        RecomputeMoveCost();

        // 4.4: autopilot springs the ambush once it has a shot (u then fires it next step
        // with the bonus), else when it has stalked within range; otherwise it keeps
        // advancing concealed. Returns so any reveal-scatter plays before the shot.
        if (SquadConcealed)
        {
            // W10 SUPPRESSOR: thread the ambush target through (like SmartStep's site) so the
            // suppressed-wake narrowing is uniform across BOTH harness policies.
            var ambush = u.Ammo > 0 ? FirstTargetFor(u) : null;
            if (ambush != null) { BreakConcealment(u, ambush, u.HasMod(WeaponMod.Suppressor)); return; }
            if (Players.Any(p => p.Alive && Enemies.Any(e => e.Alive
                    && Util.TileDist(p.X, p.Y, e.X, e.Y) <= AlertRange + 1))) { BreakConcealment(); return; }
        }

        // EVAC objective: get everyone to the extraction zone
        if (Objective == Objective.Evac)
        {
            // drop the forward beacon once the point man is past mid-field (cuts the march to the corner)
            if (CanBeacon(u) && u.X >= Grid.W / 2 && DistToEvac(u.X, u.Y) > 2) { DoBeacon(); return; }
            if (!EvacZone.Contains((u.X, u.Y)))
            {
                var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                   .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
                if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return;
            }
            // can't make extraction progress this turn — clear blockers / re-arm
            var et = FirstTargetFor(u);
            if (et != null && u.Ammo > 0) { IssueShoot(et); return; }
            // an enemy squatting on the extraction zone: blast it loose
            if (u.Grenades > 0)
            {
                var blocker = AliveEnemies()
                    .Where(e => EvacZone.Contains((e.X, e.Y)) && Util.TileDist(u.X, u.Y, e.X, e.Y) <= GrenadeRange)
                    .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
                if (blocker != null) { IssueGrenade(blocker.X, blocker.Y); return; }
            }
            if (u.Ammo == 0) { DoReload(); return; }   // re-arm instead of stalling forever
            DoHunker();
            return;
        }

        // HACK objective: get a soldier to the terminal and hack it down
        if (Objective == Objective.Hack)
        {
            if (CanHack(u)) { DoHack(); return; }
            var et = FirstTargetFor(u);
            if (et != null && u.Ammo > 0) { IssueShoot(et); return; }
            if (TryMoveTowardTile(u, Terminal.x, Terminal.y)) return;
            DoHunker();
            return;
        }

        // SABOTAGE objective: plant charges on each site in turn
        if (Objective == Objective.Sabotage)
        {
            if (CanHack(u)) { DoHack(); return; }
            var et = FirstTargetFor(u);
            if (et != null && u.Ammo > 0) { IssueShoot(et); return; }
            var site = SabotageSites
                .Where((s, i) => !SabotageBlown.Contains(i))
                .OrderBy(s => Util.TileDist(u.X, u.Y, s.x, s.y)).FirstOrDefault();
            if (site != default && TryMoveTowardTile(u, site.x, site.y)) return;
            DoHunker();
            return;
        }

        // ESCORT objective: walk the VIP to extraction; soldiers screen for it.
        // W4 (SIGNAL): freed-state RESCUE is the same mission shape (leashed asset to the zone),
        // so it shares this block — the old freed-captive self-race fought the new leash (tug-of-
        // war every turn boundary), and rescue soldiers never marched to evac at all (a cleared
        // board would stall to the turn cap with the leash-held captive parked beside them).
        if (Objective == Objective.Escort || (Objective == Objective.Rescue && !CaptiveLocked))
        {
            if (u.IsVip)
            {
                // the LEASH (LeashVip) tags the asset along with the squad at each turn boundary — the
                // VIP no longer self-walks to the corner (which would fight the leash). Just tuck in.
                DoHunker(); return;
            }
            // APEX W8: drop the forward beacon once a screener has pushed into the far third with a
            // cold LZ (CanBeacon's Escort gate) — the leashed VIP then extracts here, not the far corner.
            if (CanBeacon(u) && u.X >= Grid.W * 2 / 3 && DistToEvac(u.X, u.Y) > 2) { DoBeacon(); return; }
            var st = FirstTargetFor(u);
            if (st != null && u.Ammo > 0) { IssueShoot(st); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            // reached the zone + adjacent to the leashed VIP? haul it aboard (the escort-ending pull).
            if (EvacZone.Contains((u.X, u.Y)) && CanExtract(u)) { DoExtract(); return; }
            // otherwise advance to the evac zone so the squad drags the leashed VIP along and can extract it.
            var ez = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                             .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
            if (ez != default && !EvacZone.Contains((u.X, u.Y)) && TryMoveTowardTile(u, ez.x, ez.y)) return;
            var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (foe != null && TryMoveTowardTile(u, foe.X, foe.Y)) return;
            DoHunker(); return;
        }

        // RESCUE objective, CAGED phase only (the freed phase shares the Escort block above):
        // converge on the cage to spring the captive, fighting through what's in the way.
        if (Objective == Objective.Rescue)
        {
            if (u.IsVip) { DoHunker(); return; }               // can't move while caged
            if (Vip != null && Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) > 1
                && TryMoveTowardTile(u, Vip.X, Vip.Y)) return;   // go spring the captive
            var rt = FirstTargetFor(u);
            if (rt != null && u.Ammo > 0) { IssueShoot(rt); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            var rfoe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (rfoe != null && TryMoveTowardTile(u, rfoe.X, rfoe.Y)) return;
            DoHunker(); return;
        }

        // DEFEND objective: hold position, shoot, overwatch, hunker until the timer runs out
        if (Objective == Objective.Defend)
        {
            var dt = FirstTargetFor(u);
            if (dt != null && u.Ammo > 0) { AutoShoot(u, dt); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            // W9 THE REPAIR — `&& !u.HasStatus(StatusKind.Disoriented)`. THIS LINE WAS A HARD HANG.
            // DoOverwatch REFUSES for a disoriented soldier (Codex: "cannot hold overwatch") and
            // returns having spent nothing, but the `return` here was unconditional — so AutoStep
            // ended the step with the action unspent, the turn never ended, and the next frame did
            // the same thing forever. Head-of-line blocking makes it total: AutoStep always picks
            // Players.FirstOrDefault(CanAct), so one disoriented soldier freezes the whole squad.
            // Measured live (autoplay seed 3001, ~1% of campaigns): mission 5 DEFEND, all enemies
            // dead, ASH disoriented with 2 actions and 3 ammo, 38,000 consecutive frames in
            // PlayerTurn with _turnCount frozen at 2 and an empty anim queue. AutoStallCheck could
            // never see it — that guard only runs in StartPlayerTurn, i.e. only at a turn boundary
            // the game can no longer reach. The frame cap was the ONLY exit, which is exactly the
            // RESULT: TIMEOUT the autopilot's own contract says is unreachable. SmartStep already
            // carried this guard at both of its overwatch sites; the smoke-test policy did not.
            if (u.ActionsLeft > 0 && u.Ammo > 0 && !u.HasStatus(StatusKind.Disoriented)) { DoOverwatch(); return; }
            DoHunker(); return;   // always spends the action -> progress is guaranteed
        }

        // DECAPITATE objective: focus the marked HVT. Shoot it on sight, grenade it if it's the
        // only reachable play, otherwise advance on it; fall through to the generic combat
        // behaviour (clear blockers / reload / hunker) so progress is always guaranteed.
        if (Objective == Objective.Decapitate)
        {
            if (Hvt != null && Hvt.Alive)
            {
                if (u.Ammo > 0 && CanTarget(u, Hvt)) { AutoShoot(u, Hvt); return; }     // kill the target
                if (u.Ammo == 0) { DoReload(); return; }
                if (u.Grenades > 0 && Util.TileDist(u.X, u.Y, Hvt.X, Hvt.Y) <= GrenadeRange
                    && !Players.Any(f => f.Alive && Util.ChebyDist(f.X, f.Y, Hvt.X, Hvt.Y) <= 1))
                { IssueGrenade(Hvt.X, Hvt.Y); return; }                                  // flush it out
                // can't hit it yet: shoot anything blocking the path, else close on the HVT
                var blk = FirstTargetFor(u);
                if (blk != null && blk != Hvt && u.Ammo > 0
                    && Util.TileDist(u.X, u.Y, blk.X, blk.Y) <= 3) { AutoShoot(u, blk); return; }
                if (TryMoveTowardTile(u, Hvt.X, Hvt.Y)) return;
            }
            // HVT already dead (CheckEnd will end the mission) or unreachable: keep generic.
            var dtgt = FirstTargetFor(u);
            if (dtgt != null && u.Ammo > 0) { AutoShoot(u, dtgt); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            var dfoe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (dfoe != null && TryMoveTowardTile(u, dfoe.X, dfoe.Y)) return;
            DoHunker(); return;
        }

        // ELIMINATE objective
        // (test) exercise the class signature ability to keep its path covered
        if (CanAbility(u) && Util.Roll(45))
        {
            var kind = u.Ability;
            // the targeting VERBS resolve directly (DoAbility would open a mode the dumb smoke-test
            // autopilot can't drive) -> exercise the IssueMark/IssueGrapple/IssuePin paths.
            if (kind == AbilityKind.Mark)
            {
                var mt = BestMarkTarget(u);
                // MARK resolves IMMEDIATELY (no anim is queued), so falling through to the shot still
                // decides from the real board — unlike GRAPPLE below.
                if (mt != null) IssueMark(u, mt);    // costs an action but not the turn -> fall through and shoot
            }
            else if (kind == AbilityKind.Grapple)
            {
                // W9 THE REPAIR: HONOUR THE QUEUE. GRAPPLE enqueues a ShoveAnim that has NOT run yet;
                // falling through to AutoShoot queued a SECOND player action behind it, decided from a
                // board that anim is about to change. That is exactly how a soldier came to fire its own
                // shot after its own grapple downed it (ShoveAnim -> EnterDowned, with the ShotAnim
                // already queued). Return instead: the soldier keeps its remaining action and uses it on
                // the next AutoStep, once the queue has drained. AbilityCd is set, so no loop.
                var gt = BestGrappleTarget(u);
                if (gt != null) { IssueGrapple(u, gt); return; }
            }
            else if (kind == AbilityKind.Pin)
            {
                var pt = BestPinTarget(u);
                if (pt != null) { IssuePin(u, pt); return; } // SUPPRESSING FIRE ends the turn (full burst)
            }
            else
            {
                DoAbility();
                if (kind == AbilityKind.Steady || kind == AbilityKind.Suppress || kind == AbilityKind.Heal) return; // spent an action
                // RunGun / Blitz / Slipstream are free stances — fall through and act with them
            }
        }

        var tgt = FirstTargetFor(u);
        if (tgt != null && u.Ammo > 0) { AutoShoot(u, tgt); return; }
        if (u.Ammo == 0) { DoReload(); return; }

        // (test) deploy the class utility item to keep its path covered
        if (u.ItemCharge > 0 && Util.Roll(30))
        {
            if (u.Item == ItemKind.Barricade)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = u.X + dx, ny = u.Y + dy;
                        if ((dx != 0 || dy != 0) && ItemTargetOk(u, nx, ny)) { IssueItem(nx, ny); return; }
                    }
            }
            else
            {
                var near = AliveEnemies().Where(e => Util.TileDist(u.X, u.Y, e.X, e.Y) <= ItemRange)
                                         .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
                if (near != null) { IssueItem(near.X, near.Y); return; }
            }
        }

        // lob a grenade at any hostile in range (exercises the AoE path)
        if (u.Grenades > 0)
        {
            var near = AliveEnemies().Where(e => Util.TileDist(u.X, u.Y, e.X, e.Y) <= GrenadeRange)
                                     .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (near != null) { IssueGrenade(near.X, near.Y); return; }
        }

        var enemy = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        if (enemy != null && TryMoveTowardTile(u, enemy.X, enemy.Y)) return;
        DoHunker(); // guarantees progress
    }

    // autopilot helper: fire at `tgt`, exercising the SNAP path so it stays covered. When the
    // soldier has both actions, ~40% take a snap (1 action, no end-turn, -aim) instead of the
    // turn-ending aimed shot; the soldier then keeps acting next AutoStep (move/second snap/
    // overwatch). Bounded: a snap always costs >=1 action, so at most two snaps end the turn —
    // no infinite loop. (The flank-kill refund is capped 1/turn, so it can't unbound this.)
    void AutoShoot(Unit u, Unit tgt)
    {
        if (u.ActionsLeft >= 2 && !u.RunGun && Util.Roll(40)) SnapShot = true;  // consumed by IssueShoot
        IssueShoot(tgt);
    }

    // autopilot helper: step toward (gx,gy) along true path distance; random hop if stuck
    bool TryMoveTowardTile(Unit u, int gx, int gy)
    {
        if (MoveCost == null) return false;
        var gd = Grid.CostMap(gx, gy, (x, y) => IsOccupiedByOther(x, y, u), out _, 9999);
        int my = gd[u.X, u.Y];
        int bx = -1, by = -1, best = int.MaxValue;
        var reachable = new List<(int, int)>();
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y];
                if (c <= 0) continue;
                int need = c <= u.MoveBudget ? 1 : 2;
                if (need > u.ActionsLeft) continue;
                reachable.Add((x, y));
                int d = gd[x, y];
                if (d >= 0 && d < best) { best = d; bx = x; by = y; }
            }
        if (bx >= 0 && (my < 0 || best < my)) { IssueMove(bx, by); return true; }
        if (reachable.Count > 0) { var (rx, ry) = Util.Choice(reachable); IssueMove(rx, ry); return true; }
        return false;
    }

    // ── W1 TRUE INSTRUMENT: the autopilot's CAMPAIGN ROUTING POLICY ──────────────────────
    // Every balance number this project has published was measured on ONE route policy, and
    // nobody ever chose it. The rule was "prefer an Event node when one is reachable, else take
    // nn[0]" — and nn[0] is simply whichever node Run.NextNodes() happened to list first, i.e.
    // the LOWEST ROW of the next column, because the edges were appended in row order. That is
    // not a neutral sample of the campaign map: the bot walks one edge of the DAG and pulls the
    // node kinds, factions and objectives that live along it, every run, for ever. byNodeKind
    // (Stats) is the read-out; this is the dial.
    //
    // SIGHTLINE_ROUTE:
    //   first (DEFAULT) — the shipped behaviour, unchanged. Every archived measurement stays
    //                     comparable; this wave does not move a single published number with it.
    //   hash            — deal the branch from Util.Hash3(MapSeed, 13, mission), the same
    //                     zero-draw hashing FUL-9 uses for the objective plan. It takes NO draws
    //                     from Util.Rng, so a CRN slot pair still replays identically under it.
    // Strict parse (the SIGHTLINE_OBJ precedent): a typo runs the DEFAULT with a loud warning,
    // never a silently different route that walks into a DEVLOG as a baseline.
    public static readonly string RoutePolicy = ParseRoutePolicy();
    static string ParseRoutePolicy()
    {
        string v = Environment.GetEnvironmentVariable("SIGHTLINE_ROUTE");
        if (string.IsNullOrEmpty(v) || v == "first") return "first";
        if (v == "hash") return "hash";
        Console.WriteLine($"ROUTE: unknown SIGHTLINE_ROUTE '{v}' — routing 'first'");
        return "first";
    }

    /// The next-node pick for a given policy. Static + parameterised so ROUTETEST can drive it
    /// over a bare Run map with no Game, no window and no draws.
    public static MissionNode PickAutoNode(List<MissionNode> nn, int mapSeed, int mission, string policy = null)
    {
        if (nn == null || nn.Count == 0) return null;
        if ((policy ?? RoutePolicy) == "hash")
            return nn[(int)(Util.Hash3(mapSeed, 13, mission) % (uint)nn.Count)];
        var ev = nn.FirstOrDefault(x => x.Kind == NodeKind.Event);
        return ev ?? nn[0];
    }

    MissionNode PickAutoNode(List<MissionNode> nn) => PickAutoNode(nn, _run.MapSeed, _run.Mission);
}

/// ── TRUE BAND: the instrument-DESIGN probe (SIGHTLINE_BANDPROBE=1) ──────────────────
/// CountMeaningfulChoices bands its two axes against a score whose SCALE nobody had ever
/// looked at. This probe records that scale: histograms of the per-soldier best score on
/// each axis (`best` for "which target", `pbest` for "where do I stand"), histograms of
/// every candidate's GAP to it, and the candidate counts that a multiplicative window and
/// several additive ones would each admit over the identical sample. It exists so the band
/// constants are chosen from the distribution instead of guessed.
/// Default OFF; when on it is allocation-free (fixed arrays), read-only, and takes no RNG
/// draw. Only reachable from the balance batch (CountMeaningfulChoices runs under
/// Stats.Enabled only), so interactive play never pays for it.
public static class ChoiceProbe
{
    public static bool On;
    public const int NegOff = 40, ScoreMax = 120;   // score bucket = round(v)+40, range -40..80
    public const int GapMax = 60;                   // gap bucket = round(best - v), clamped
    public static readonly long[] ShotBest = new long[ScoreMax + 1];
    public static readonly long[] ShotGap = new long[GapMax + 1];
    public static readonly long[] PosBest = new long[ScoreMax + 1];
    public static readonly long[] PosGap = new long[GapMax + 1];
    // axis (a): armed soldier-turns scored, total candidate shots, and how many candidates
    // each window admits (mult 0.88 = the pre-wave rule; add-N = within N value points).
    public static long ShotTurns, ShotCands, ShotMult, ShotAdd1, ShotAdd2, ShotAdd3, ShotAdd4;
    // axis (b): soldier-turns reaching the positioning axis, how many were SKIPPED by the
    // pre-wave `pbest > 0` guard (a threat-coupled disqualifier), candidates, and windows.
    public static long PosTurns, PosSkippedGuard, PosCands, PosMult, PosAdd2, PosAdd3, PosAdd4, PosAdd5;
    // capped-contribution sums, so the effect of the anti-inflation cap is visible: raw
    // (n-1) vs the shipped min(2, n-1), under the additive-3 rule.
    public static long PosRawAdd3, PosCap2Add3, PosCap4Add3;
    // TRUE BAND review fix: the FULL 2x2 decomposition of axis (b) — {mult window, additive
    // band} x {cap 2, cap 4} — accumulated over the SAME soldier-turns and therefore with the
    // SAME denominator. Without this the band's effect and the cap's effect could only be
    // compared across two different denominators (the probe's PosTurns vs Stats'
    // ArmedSoldierTurns), which is how the wave's first write-up attributed the cap's lift to
    // the band. MultCap2 IS the pre-wave rule (guard included); AddCap4 IS the shipped one.
    public static long PosMultCap2, PosMultCap4, PosAddCap2, PosAddCap4;
    // per-turn ADMITTED-COUNT histograms (clamped at 40): the distribution the anti-inflation
    // cap actually clips. Choosing the cap needs this, not the mean.
    public const int AdmitMax = 40;
    public static readonly long[] AdmitAdd3 = new long[AdmitMax + 1];
    public static readonly long[] AdmitMult = new long[AdmitMax + 1];

    public static void Score(long[] h, float v)
    { int i = (int)MathF.Round(v) + NegOff; h[i < 0 ? 0 : (i > ScoreMax ? ScoreMax : i)]++; }
    public static void Gap(long[] h, float d)
    { int i = (int)MathF.Round(d); h[i < 0 ? 0 : (i > GapMax ? GapMax : i)]++; }

    static string Pct(long[] h, int off)
    {
        long n = 0; foreach (var c in h) n += c;
        if (n == 0) return "(no data)";
        var qs = new[] { 0.05, 0.25, 0.50, 0.75, 0.95 };
        var outp = new System.Text.StringBuilder();
        int qi = 0; long run = 0;
        for (int i = 0; i < h.Length && qi < qs.Length; i++)
        {
            run += h[i];
            while (qi < qs.Length && run >= qs[qi] * n)
            { outp.Append($"p{qs[qi] * 100:0}={i - off} "); qi++; }
        }
        double mean = 0; for (int i = 0; i < h.Length; i++) mean += (double)h[i] * (i - off);
        return $"n={n} mean={mean / n:0.00} {outp}".TrimEnd();
    }

    public static string Report()
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine("── CHOICE-BAND PROBE (SIGHTLINE_BANDPROBE) ───────────────────────────────");
        s.AppendLine($"axis(a) which-target : armedTurns={ShotTurns} cands={ShotCands} ({(double)ShotCands / Math.Max(1, ShotTurns):0.00}/turn)");
        s.AppendLine($"  best-shot-value    : {Pct(ShotBest, NegOff)}");
        s.AppendLine($"  gap(best-v)        : {Pct(ShotGap, 0)}");
        s.AppendLine($"  admitted/turn      : mult.88={(double)ShotMult / Math.Max(1, ShotTurns):0.000}  add1={(double)ShotAdd1 / Math.Max(1, ShotTurns):0.000}  add2={(double)ShotAdd2 / Math.Max(1, ShotTurns):0.000}  add3={(double)ShotAdd3 / Math.Max(1, ShotTurns):0.000}  add4={(double)ShotAdd4 / Math.Max(1, ShotTurns):0.000}");
        s.AppendLine($"axis(b) where-to-stand: turns={PosTurns} guardSkipped={PosSkippedGuard} ({100.0 * PosSkippedGuard / Math.Max(1, PosTurns):0.0}%) cands={PosCands} ({(double)PosCands / Math.Max(1, PosTurns):0.0}/turn)");
        s.AppendLine($"  best-safety(pbest) : {Pct(PosBest, NegOff)}");
        s.AppendLine($"  gap(pbest-s)       : {Pct(PosGap, 0)}");
        s.AppendLine($"  admitted/turn      : mult.85={(double)PosMult / Math.Max(1, PosTurns):0.00}  add2={(double)PosAdd2 / Math.Max(1, PosTurns):0.00}  add3={(double)PosAdd3 / Math.Max(1, PosTurns):0.00}  add4={(double)PosAdd4 / Math.Max(1, PosTurns):0.00}  add5={(double)PosAdd5 / Math.Max(1, PosTurns):0.00}");
        s.AppendLine($"  contribution/turn  : add3 raw={(double)PosRawAdd3 / Math.Max(1, PosTurns):0.000}  cap2={(double)PosCap2Add3 / Math.Max(1, PosTurns):0.000}  cap4={(double)PosCap4Add3 / Math.Max(1, PosTurns):0.000}");
        s.AppendLine($"  2x2 decomposition  : multCap2={(double)PosMultCap2 / Math.Max(1, PosTurns):0.000} (PRE-WAVE)  addCap2={(double)PosAddCap2 / Math.Max(1, PosTurns):0.000}  multCap4={(double)PosMultCap4 / Math.Max(1, PosTurns):0.000}  addCap4={(double)PosAddCap4 / Math.Max(1, PosTurns):0.000} (SHIPPED)");
        s.AppendLine($"  band effect @cap2  : {(double)(PosAddCap2 - PosMultCap2) / Math.Max(1, PosTurns):+0.000;-0.000}   cap effect @add: {(double)(PosAddCap4 - PosAddCap2) / Math.Max(1, PosTurns):+0.000;-0.000}");
        s.AppendLine($"  admitted/turn dist : add3 {Pct(AdmitAdd3, 0)}");
        s.AppendLine($"                     : mult {Pct(AdmitMult, 0)}");
        return s.ToString();
    }
}
