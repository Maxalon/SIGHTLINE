using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// Base class for queued, sequential animations that can mutate game state.
public abstract class Anim
{
    public bool Started;
    public virtual void OnStart(Game g) { }
    public abstract bool Update(Game g, float dt); // return true when finished
    public virtual void Draw(Game g) { }
}

/// THE STRIDE: where a MoveStepAnim sits in its walk. Presentation only — the sim never reads it.
public enum StepSeg { Single, First, Mid, Last }

/// Move a single unit one tile along its path; checks overwatch on arrival.
public class MoveStepAnim : Anim
{
    public Unit Unit;
    public int Tx, Ty;
    Vector2 _from, _to;
    float _t, _dur;

    public MoveStepAnim(Unit u, int tx, int ty) { Unit = u; Tx = tx; Ty = ty; }

    // ── THE STRIDE — the DRAWN stride, decoupled from the COMMIT clock ────────────────────────
    // Everything in this block is presentation. None of it is read by the sim, none of it draws
    // from Util.Rng, and none of it touches the commit clock (_t / _dur -> k >= 1 -> tile entry ->
    // OnUnitEnteredTile / overwatch), which is why the autoplay frame counts and PAIRTEST identity
    // are unchanged by the tween below. Before this, every tile of a walk eased in AND out and
    // re-kicked the lean, so a six-tile move was six separate lunges — the caterpillar.
    public StepSeg Seg = StepSeg.Single;
    /// Shared by every step of one walk (Game.EnqueuePath builds it): [0] = where the figure stood,
    /// [i+1] = the centre of step i's tile. The First step overwrites [0] with the live Unit.Pos at
    /// activation, so a walk starts from wherever the figure is DRAWN — a stale origin cannot snap it.
    public List<Vector2> Path;
    public int Index;
    /// VAULT: px of peak lift, and how long the leap is DRAWN over. The commit stays on _dur, so the
    /// tile entry — and any overwatch it draws — fires at exactly the frame it did before.
    public float Hop, VisDur;
    float _period = -1f;     // the wall-clock length this step's commit will actually take at the frame rate seen (predicted on the first frame)
    float _tau0;             // stride clock at activation (carried in from the previous step of the walk)
    bool _committed;

    public const float StrideRamp  = 0.5f;    // push-off / brake, in step-times (a ONE-tile walk of this profile IS Util.EaseInOutQuad)
    public const float VaultHop    = 26f;     // px of lift at the top of a vault
    public const float VaultVisDur = 0.24f;   // s a vault is DRAWN over (its commit stays at _dur)

    /// The commit clock needs ceil(_dur / dt) frames: a 0.12 s step at 60 Hz commits on its 8th
    /// frame (0.1333 s), so 7.2 frames of motion were squeezed into 8 and the 0.8 of a frame lost
    /// at every tile boundary was a hitch on top of the easing (the "0.1 px" frame in FEELTEST's
    /// pre-fix profile). The drawn stride runs on the PREDICTED commit period instead, so it reaches
    /// each centre on the commit frame at one constant speed. A frame-time jitter makes the
    /// prediction miss by a frame; the stride clock is carried across steps (Unit.StrideTau), so a
    /// miss is a one-frame lead or lag the next step absorbs — never a snap back, never a stall.
    static float PredictPeriod(float dur, float dt) => MathF.Max(dur, MathF.Ceiling(dur / dt - 0.001f) * dt);

    /// Arc length (in STEPS) along the walk at stride clock `tau` (in step-times): a linear speed
    /// ramp over the first StrideRamp step-times, one constant stride, and the mirror-image brake.
    /// One push-off and one stop per WALK instead of one per tile. The stride speed is n/(n-ramp),
    /// so the figure trails the commit clock by at most ramp/2 of a step (16 px) during the push-off
    /// and leads it by the same on the brake — and by nothing at all in between.
    static float StrideS(float tau, int n)
    {
        float a = MathF.Min(StrideRamp, n * 0.5f);
        float v = n / (n - a);
        if (tau <= 0f) return 0f;
        if (tau >= n) return n;
        if (tau < a) return v * tau * tau / (2f * a);
        if (tau <= n - a) return v * (tau - a * 0.5f);
        float r = n - tau;
        return n - v * r * r / (2f * a);
    }

    Vector2 PathPoint(float s)
    {
        int n = Path.Count - 1;
        if (s <= 0f) return Path[0];
        if (s >= n) return Path[n];
        int i = (int)s;
        return Vector2.Lerp(Path[i], Path[i + 1], s - i);
    }

    // THE STRIDE — SIGHTLINE_FEELTEST thresholds (Game.Harness.FeelSelfTest reads them; they live
    // beside the tween they gate). Per-frame speed is measured on Unit.Pos at the harness's 1/60 dt.
    public const float FeelMidBand       = 0.5f;    // tiles excluded at each END of a walk (departure ramp / arrival brake)
    public const float FeelMinSpeedRatio = 0.60f;   // mid-path: slowest frame must be >= this x the fastest frame
    public const float FeelStallRatio    = 0.40f;   // mid-path: a frame under this x the mean speed is a STALL (zero allowed)
    public const float FeelVaultLiftMin  = 20f;     // px of peak Y lift a VAULT must show mid-flight

    /// Q1 STACKTEST probe (harness-only; ALWAYS null in normal play, so this costs one null
    /// check per step). Fires the instant a step becomes the ACTIVE anim — i.e. the moment the
    /// destination is committed to — which is exactly where a stale-plan collision is provable:
    /// Unit.X/Y is still the ORIGIN here, so `g.UnitAt(Tx,Ty)` non-null-and-not-self means this
    /// step is about to bury a living unit. Only one anim is ever active, so the occupant is
    /// stationary and the read is exact (no mid-move false positives).
    public static Action<Game, MoveStepAnim> StackProbe;

    public override void OnStart(Game g)
    {
        StackProbe?.Invoke(g, this);
        _from = Unit.Pos;
        _to = Util.TileCenter(Tx, Ty);
        bool diag = Tx != Unit.X && Ty != Unit.Y;
        _dur = diag ? 0.155f : 0.12f;
        bool walk = Path != null && Seg != StepSeg.Single;
        if (walk)
        {
            if (Seg == StepSeg.First) { Path[0] = Unit.Pos; _tau0 = 0f; }
            else _tau0 = Unit.StrideTau;
        }
        // face along the SEGMENT, not from the drawn position: mid-walk the figure trails or leads
        // its tile by a few px, and a corner turned from there would come out a few degrees short.
        var d = walk ? Path[Index + 1] - Path[Index] : _to - _from;
        if (d.LengthSquared() > 0.01f) Unit.Facing = MathF.Atan2(d.Y, d.X);
        // THE STRIDE: the lean is the PUSH-OFF (Renderer reads it as a forward body tilt; it decays
        // in Game.DecayUnitFx), so it is kicked once per walk — First/Single — not once per tile: the
        // re-kick every 0.12 s was the pumping half of the caterpillar. A vault crouches harder
        // (1.5: the same decay holds it through the 0.24 s leap).
        if (Seg == StepSeg.Single || Seg == StepSeg.First) Unit.WalkLean = Hop > 0f ? 1.5f : 1f;
        g.Fx.Dust(_from + new Vector2(0, 8f), 3);   // a small puff kicks up as the foot leaves
        // RESONANCE A2 — a footfall PER TILE, panned to where the step actually happens.
        // Audio.Play("move") used to fire once per move COMMAND, so a six-tile sprint got a
        // single 80 ms thud at the start and then silence: movement, the most frequent action
        // in the game, was effectively unvoiced. Diagonals are a touch heavier (longer stride).
        //
        // This belongs in OnStart and NOWHERE else: OnStart runs when an anim becomes ACTIVE
        // (Game.cs's single `a.Started` site), never at Enqueue — enqueue-time OnStart is the
        // old movement-jitter bug, and it would also fire a whole path's footfalls at once.
        Audio.Play("move", pitchVar: diag ? 0.085f : 0.07f,
                   panX: Util.Clamp(_from.X / Cfg.ScreenW, 0f, 1f));
    }

    public override bool Update(Game g, float dt)
    {
        if (_period < 0f && dt > 0f) _period = PredictPeriod(_dur, dt);
        _t += dt;
        float k = Util.Clamp(_t / _dur, 0f, 1f);                     // the COMMIT clock — untouched
        bool walk = Path != null && Seg != StepSeg.Single;
        bool visDone;
        if (walk)
        {
            // the drawn stride: one profile over the whole walk, on the predicted commit period
            float tau = _tau0 + _t / (_period > 0f ? _period : _dur);
            Unit.StrideTau = tau;
            Unit.Pos = PathPoint(StrideS(tau, Path.Count - 1));
            visDone = k >= 1f;
        }
        else if (Hop > 0f && VisDur > _dur)
        {
            // VAULT: a crouch-spring-land arc over the cover. Unit.Pos is what the renderer draws, so
            // the lift goes into it; Unit.HopLift lets the shadow stay on the ground underneath.
            float kv = Util.Clamp(_t / VisDur, 0f, 1f);
            float lift = MathF.Sin(kv * MathF.PI) * Hop;
            Unit.Pos = Vector2.Lerp(_from, _to, Util.EaseInOutQuad(kv)) - new Vector2(0f, lift);
            Unit.HopLift = lift;
            visDone = kv >= 1f;
        }
        else
        {
            Unit.Pos = Vector2.Lerp(_from, _to, Util.EaseInOutQuad(k));   // Single: today's tween, exactly
            visDone = k >= 1f;
        }
        if (k >= 1f && !_committed)
        {
            _committed = true;
            Unit.X = Tx; Unit.Y = Ty;
            // the figure lands on the exact centre at the END of a walk (or of a one-tile step);
            // mid-walk it keeps its stride — a trail/lead of <= 16 px the next step absorbs.
            if (visDone && (!walk || Seg == StepSeg.Last)) Unit.Pos = _to;
            g.OnUnitEnteredTile(Unit);
            // a walk cut short right here (the tile entry drew a reaction that killed or downed the
            // mover, and its later steps were purged) must not leave the figure a stride off its tile
            if (walk && Seg != StepSeg.Last && !g.HasQueuedStep(Unit, this)) Unit.Pos = _to;
        }
        if (!(_committed && visDone)) return false;
        if (Hop > 0f)
        {
            // touchdown: exactly on the centre, shadow back under the feet, a puff and a heavier footfall
            Unit.Pos = _to; Unit.HopLift = 0f;
            g.Fx.Dust(_to + new Vector2(0, 8f), 5);
            Audio.Play("move", pitchVar: 0.10f, panX: Util.Clamp(_to.X / Cfg.ScreenW, 0f, 1f));
        }
        return true;
    }
}

/// SHOVE (forced-movement verb): a soldier slams an adjacent enemy one tile directly away.
/// If the destination is clear the enemy SLIDES there (its overwatch breaks + hunker clears,
/// and it's naturally exposed for follow-up fire). If the destination is BLOCKED it doesn't
/// move and takes collision damage (and rams any unit in the way). All resolution is applied
/// in OnStart (when this becomes the ACTIVE anim) via Game so kills/death are handled correctly.
public class ShoveAnim : Anim
{
    public Unit Shover, Target;
    public int Dx, Dy;          // shove direction (one of the 8 dirs, normalized to -1/0/1)
    bool _moves;                // destination is clear -> the target slides
    int _tx, _ty;               // destination tile (only used when _moves)
    Vector2 _from, _to;
    float _t;
    const float Dur = 0.16f;    // a quick, punchy slam

    public ShoveAnim(Unit shover, Unit target, int dx, int dy) { Shover = shover; Target = target; Dx = dx; Dy = dy; }

    public override void OnStart(Game g)
    {
        // face the shover toward the target (the lunge reads in the direction of the push)
        var face = Target.Pos - Shover.Pos;
        if (face.LengthSquared() > 0.01f) Shover.Facing = MathF.Atan2(face.Y, face.X);
        Shover.WalkLean = 1f;
        Shover.Recoil = new Vector2(Dx, Dy) * 5f;          // a small lunge forward

        if (!Target.Alive) { _moves = false; _from = _to = Target.Pos; return; }

        _tx = Target.X + Dx; _ty = Target.Y + Dy;
        _moves = g.Grid.IsFloor(_tx, _ty) && !g.IsOccupiedByOther(_tx, _ty, Target);

        // being shoved always breaks the target's set stance (it's caught off balance).
        Target.OnOverwatch = false;
        Target.Hunkered = false;
        Target.FlinchAnim = MathF.Max(Target.FlinchAnim, 0.7f);

        _from = Target.Pos;
        _to = _moves ? Util.TileCenter(_tx, _ty) : Target.Pos;

        Audio.Play("hunker");
        g.Fx.AddShake(3f);
        g.AddHitStop(0.03f);

        if (_moves)
        {
            // dust kicked along the slide path
            g.Fx.Dust(_from + new Vector2(0, 8f), 4);
            g.Fx.PopText(_from + new Vector2(0, -28), "SHOVED", Pal.Friend, 18f);
        }
        else
        {
            // blocked: a collision burst at the wall/obstacle + knockback kick back at the shover
            var hitPt = Target.Pos + new Vector2(Dx, Dy) * (Cfg.Tile * 0.45f);
            g.Fx.Burst(hitPt, Pal.RGBA(220, 200, 140), 12, 180f, 0.45f, 3.5f, true);
            g.Fx.Impact(hitPt, Pal.RGBA(255, 240, 220), 14f, 0.7f, 0.12f);
            Target.Recoil = new Vector2(-Dx, -Dy) * 5f;     // bounces off the obstacle

            // collision damage to the shoved enemy; if it was rammed INTO another unit, that unit
            // takes a lighter hit too. EnvDamage handles FX + kill/near-death + KillUnit.
            // W9 THE REPAIR: the INITIATOR is never "rammed" by its own forced-movement verb.
            // GRAPPLE pulls TOWARD the grappler, so a Chebyshev-1 foe's destination tile IS the
            // grappler's own tile — the slide is blocked, `rammed` resolved to the GRAPPLER, and the
            // soldier who spent the action and the cooldown took ShoveRammedDamage from its own
            // GRAPPLE. Reproduced live on autoplay seed 3406: the grappler killed ITSELF (VEGA, hp
            // 1 -> 0) with the target never moving. QA's verifier measured the wider rate at 3 of 3
            // Chebyshev-1 grapples across 11 campaigns.
            // That is also 100% of a JUGGERNAUT's grapples — GrappleReachFor pins that fork at reach 1.
            // Excluding the shover makes the adjacent case a clean SLAM: the foe still takes the
            // collision damage and still loses overwatch/hunker, which is a real (if lesser) use of the
            // verb, so JUGGERNAUT keeps a working signature ability. Inert for SHOVE (its vector points
            // AWAY, so the shover is never in the destination tile) and for DRAG (DragTargetOk already
            // refuses a destination equal to the dragger's tile).
            var rammed = g.UnitAt(_tx, _ty);   // null if blocked by terrain/edge rather than a unit
            g.EnvDamage(Target, Math.Max(1, Combat.ShoveCollisionDamage), "SLAM", Pal.RGBA(255, 210, 150));
            if (rammed != null && rammed.Alive && rammed != Target && rammed != Shover)
                g.EnvDamage(rammed, Math.Max(1, Combat.ShoveRammedDamage), "SLAM", Pal.RGBA(255, 210, 150));
        }
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_moves) return _t >= Dur;       // blocked shove: just a brief beat (damage done in OnStart)
        if (!Target.Alive) return true;      // a collision/ram kill could have removed it
        float k = Util.Clamp(_t / Dur, 0f, 1f);
        Target.Pos = Vector2.Lerp(_from, _to, Util.EaseOutQuad(k));
        if (k >= 1f)
        {
            Target.X = _tx; Target.Y = _ty;
            Target.Pos = _to;
            g.OnUnitEnteredTile(Target);     // bleed/overwatch checks fire on the new tile, like any move
            return true;
        }
        return false;
    }
}

/// A fired shot: recoil, muzzle flash, tracer beam, then resolve damage.
public class ShotAnim : Anim
{
    public Unit A, D;
    public ShotResult Res;
    public bool Reaction;
    // UNDERTOW W2: a BRACE reaction. On a hit this shot STAGGERS the target (zeroes its remaining
    // actions this turn) instead of trying to kill it — set when the reacting soldier was braced.
    public bool Stagger;

    // A shot reads as a 3-beat: a brief WIND-UP (anticipation — reticle snaps in, muzzle
    // charges) -> FIRE (muzzle/tracer/impact land) -> SETTLE. The wind-up is kept short so
    // play doesn't drag; under AutoPlay it's skipped (Anim Total is unchanged either way).
    const float WindUp = 0.10f;
    // THE BEAT — a REACTION winds up longer. `Reaction` was set by Game.OnUnitEnteredTile and read
    // by no presentation code: a reaction fired on the same 0.10 s wind-up as any shot, with no
    // hit-stop and no cue of its own, so "caught in the open" looked like any other shot. Now the
    // mover gets a snap-freeze, a bigger team-tinted reticle, a flash and a flinch (OnStart), and the
    // round leaves at ~0.30 s instead of 0.14. Windless (AutoPlay) collapses all of it.
    public const float ReactWindUp  = 0.26f;
    public const float ReactHitStop = 0.06f;
    float _windUp = WindUp;
    float Fire => _windUp + 0.04f;       // muzzle/tracer/Apply fire just after the wind-up
    const float Total = 0.52f;           // the windless / plain-shot length (unchanged since 3.11)
    // the beam and the settle are laid out FROM the fire beat, so a longer wind-up shifts them
    // instead of eating them: plain 0.14 -> 0.34 -> 0.52 exactly as before; reaction 0.30 -> 0.50
    // -> 0.68. Windless keeps Total so the harness's frame counts do not move.
    float BeamEndAt => FireAt + 0.20f;
    float TotalAt => _windless ? Total : FireAt + 0.38f;
    float _t;
    bool _applied;
    Vector2 _impact;
    bool _windless;        // AutoPlay: collapse the wind-up so the smoke/balance harness stays fast

    public ShotAnim(Unit a, Unit d, ShotResult res, bool reaction = false)
    { A = a; D = d; Res = res; Reaction = reaction; }

    // effective fire time — under AutoPlay the wind-up anticipation is dropped so headless
    // runs don't slow (the visual beat is purely for a human watching).
    float FireAt => _windless ? 0.04f : Fire;
    /// THE BEAT — harness reads (FEELTEST leg e): when the round leaves and when the anim ends,
    /// as this instance will actually play them (OnStart decides windless / reaction).
    public float FireAtSecs => FireAt;
    public float TotalSecs => TotalAt;

    public override void OnStart(Game g)
    {
        var dir = D.Pos - A.Pos;
        if (dir.LengthSquared() > 0.01f) A.Facing = MathF.Atan2(dir.Y, dir.X);
        _impact = D.Pos;
        _windless = g.AutoPlay;
        // P14 — THE REACTION ANNOUNCES ITSELF WHEN IT HAPPENS, NOT WHEN IT IS QUEUED.
        // Game.OnUnitEnteredTile used to pop the "OVERWATCH"/"BRACE" text and play the reaction cue
        // inside its watcher loop, i.e. for EVERY watcher on the tile-entry frame, while the beat
        // those announce plays here — one reaction at a time, 0.68 s apart for a reaction. Two
        // watchers therefore fired two cues on one frame and then two shots over 1.36 s, and a
        // watcher whose queued shot was later purged (the mover died to the first) had already
        // announced a reaction the player never saw. It lives here now: same frame as the freeze,
        // the reticle and the tracer, once per reaction that actually happens, panned to the
        // watcher. Outside the `_windless` block on purpose — AutoPlay collapses the VISUALS, and
        // an audio call is a no-op with no device, so the cue accounting stays the same in both.
        if (Reaction)
        {
            g.Fx.PopText(A.Pos + new Vector2(0, -30), Stagger ? "BRACE" : "OVERWATCH",
                         Stagger ? Pal.Good : Pal.Accent, 18f);
            Audio.Cue(Audio.GameEvent.OverwatchFires,
                      panX: Util.Clamp(A.Pos.X / (float)Cfg.ScreenW, 0f, 1f));
        }
        if (!_windless)
        {
            Color ret = A.Team == Team.Player ? Pal.Friend : Pal.Foe;
            if (Reaction)
            {
                // THE BEAT — CAUGHT IN THE OPEN. The reaction is the one shot the player did not
                // order (or did not expect): a short snap-freeze the instant the watcher answers, a
                // bigger reticle closing on the MOVER, a flash of the watcher's colour on the mover
                // and a flinch as it realises — then the longer wind-up plays out.
                _windUp = ReactWindUp;
                g.AddHitStop(ReactHitStop);
                g.Fx.ReticleSnap(D.Pos, ret, 44f, 16f, 0.9f, _windUp + 0.02f);
                g.Fx.Flash(D.Pos, ret, 24f, 0.18f, 0.5f);
                D.FlinchAnim = MathF.Max(D.FlinchAnim, 0.6f);
            }
            else
            {
                // anticipation: a reticle snaps onto the target over the wind-up beat, tinted to
                // the firer's team, so the eye is drawn to the impact point before the round flies.
                g.Fx.ReticleSnap(D.Pos, ret, 26f, 13f, 0.7f, WindUp + 0.02f);
            }
        }
    }

    public override bool Update(Game g, float dt)
    {
        // W9 THE REPAIR: a body does not shoot. Game.PurgeAnimsFor now drops queued shots whose
        // ATTACKER was just felled, but this is the independent second guard — the one that also
        // covers a shooter that dies between this anim becoming active and its FireAt beat, and any
        // future path that queues a shot without going through the purge. Self-cancelling here
        // (rather than inside Apply) also skips the muzzle/tracer, so nothing at all fires.
        if (A == null || !A.Alive || A.Downed) return true;
        _t += dt;
        if (!_applied && _t >= FireAt)
        {
            _applied = true;
            Apply(g);
        }
        return _t >= TotalAt;
    }

    void Apply(Game g)
    {
        var dir = Vector2.Normalize(D.Pos - A.Pos + new Vector2(0.001f, 0f));
        Color muzzleCol = A.Team == Team.Player ? Pal.Friend : Pal.Foe;
        g.Fx.Muzzle(A.Pos, dir, Pal.Accent);
        // a transient additive muzzle LIGHT at the barrel — flares the moment of fire so the
        // shot reads as a burst of light (the bloom bright-pass haloes it). Crit kicks brighter.
        Vector2 mouth = A.Pos + dir * 16f;
        g.Fx.Flash(mouth, Pal.Accent, (Res.Hit && Res.Crit) ? 20f : 15f, 0.10f, 0.5f);
        // Graze shakes less than a solid hit.
        g.Fx.AddShake(Res.Hit ? (Res.Graze ? 2f : (Res.Crit ? 9f : 5f)) : 2.5f);
        // per-shot pitch variation + stereo pan so repeated fire doesn't sound identical: pan
        // by the firer's screen-x, pitch by a small deterministic jitter (handled in Audio).
        float panX = Util.Clamp(A.Pos.X / (float)Cfg.ScreenW, 0f, 1f);
        Audio.PlayWeapon(A.Weapon.Kind, 0.06f, panX);   // per-weapon firing voice (rifle/shotgun/sniper/lmg/smg)
        Audio.Play(Res.Hit ? (Res.Crit ? "crit" : "hit") : "miss", 0.05f, Util.Clamp(D.Pos.X / (float)Cfg.ScreenW, 0f, 1f));
        // balance telemetry (no-op unless Stats.Enabled): one record per resolved shot, here
        // where the ShotResult is final. dmg counts only when the round connects.
        Stats.RecordShot(A.Cls, (int)A.Team, Res.Hit, Res.Crit, Res.Graze, Res.Hit ? Res.Damage : 0);
        A.Recoil = -dir * (A.Weapon.Kind == WeaponKind.Shotgun ? 9f : 6f); // kick back
        A.RecoilAnim = A.Weapon.Kind == WeaponKind.Shotgun ? 1f : 0.8f;    // fire-recoil POSE: body rocks back along -Facing (Renderer); decays in Game.Update

        if (Res.Hit)
        {
            _impact = D.Pos;
            D.Hp -= Res.Damage;

            // VENOM boon: a player's hit leaves the (surviving) enemy bleeding (DoT per step).
            if (A.Team == Team.Player && D.Team == Team.Enemy && D.Hp > 0 && g.HasBoon(Sightline.Boon.Venom))
                D.AddStatus(StatusKind.Bleed, 2);

            if (Res.Graze)
            {
                // Graze: wing-clip — lighter flash, less knockback, no cover chip; a tiny
                // hit-stop so it still registers as contact (snappier than a solid hit).
                D.Flash = 0.5f;
                D.Recoil = dir * 2.5f;
                D.FlinchAnim = MathF.Max(D.FlinchAnim, 0.5f);   // a small shudder (Renderer); decays in Game.Update
                g.AddHitStop(0.03f);                            // graze: lightest freeze of the ladder
                g.AddBloom(0.02f);
                Color grazeTint = D.Team == Team.Player ? Pal.Friend : Pal.Foe;
                g.Fx.Burst(D.Pos, grazeTint, 5, 110f, 0.35f, 2.5f, true);
                // small impact frame + a thin directional spray + a short faint smear
                g.Fx.Impact(_impact, Pal.RGBA(220, 226, 236), 9f, 0.55f, 0.10f);
                g.Fx.Flash(_impact, Pal.RGBA(210, 220, 235), 9f, 0.09f, 0.32f);   // a faint impact spark light
                g.Fx.DirSparks(_impact, dir, grazeTint, 5, 150f, 0.55f, 2.2f);
                g.Fx.ImpactStreak(_impact, dir, Pal.RGBA(210, 218, 230), 16f, 2.2f, 0.5f, 0.08f);
                g.Fx.PopText(D.Pos + new Vector2(0, -26), "GRAZE", Pal.RGBA(190, 200, 215), 20f);
            }
            else
            {
                bool willKill = D.Hp <= 0;                        // this blow drops the target (HP already applied above)
                g.TryChipCover(A, D);                              // heavy weapons chew the target's cover (3.6)
                D.Flash = 1f;
                D.Recoil = dir * (Res.Crit ? 8f : 5f);           // knockback
                D.FlinchAnim = Res.Crit ? 1f : 0.85f;            // hit-flinch POSE: harder on a crit (Renderer); decays in Game.Update
                // Graded hit-stop ladder: a normal hit is SNAPPY (0.05s), a crit lands
                // heavier (0.10s). A kill's freeze is owned by KillUnit (>=0.10s, kill-cam
                // 0.40s) and AddHitStop is a Max, so it always dominates -> a kill reads as
                // the weightiest beat without us double-counting here.
                g.AddHitStop(Res.Crit ? 0.10f : 0.05f);
                // bloom: crit spikes hotter than a hit; a KILL spikes hardest of the per-shot
                // tier (still under the kill-cam) so the flash punches even off the kill-cam.
                g.AddBloom(willKill ? 0.13f : (Res.Crit ? 0.09f : 0.045f));
                Color blood = D.Team == Team.Player ? Pal.Friend : Pal.Foe;
                g.Fx.Burst(D.Pos, blood, Res.Crit ? 22 : 13, Res.Crit ? 320f : 200f, 0.5f, 3.5f, true);
                g.Fx.Burst(D.Pos, Pal.RGBA(230, 230, 235), 6, 120f, 0.4f, 2.5f);

                // Impact frame + directional sparks + smear, scaled by outcome: a crit throws
                // a big hot burst + a dense, faster, tighter spray + a long bright smear; a
                // normal hit a medium one. A KILL gets an extra escalation below.
                if (Res.Crit)
                {
                    g.Fx.Impact(_impact, Pal.Accent, 26f, 0.95f, 0.16f);
                    g.Fx.Flash(_impact, Pal.Accent, 30f, 0.14f, 0.6f);   // bright crit impact light
                    g.Fx.DirSparks(_impact, dir, Pal.Accent, 16, 360f, 0.5f, 3.4f);
                    g.Fx.DirSparks(_impact, dir, Pal.RGBA(255, 250, 240), 6, 280f, 0.35f, 2.6f);
                    g.Fx.ImpactStreak(_impact, dir, Pal.Accent, 40f, 4.2f, 0.9f, 0.12f);
                    g.Fx.AddShake(1.5f);                          // a sharper extra kick on a crit
                }
                else
                {
                    g.Fx.Impact(_impact, Pal.RGBA(255, 240, 235), 16f, 0.85f, 0.12f);
                    g.Fx.Flash(_impact, Pal.RGBA(255, 238, 226), 17f, 0.11f, 0.45f);   // hit impact light
                    g.Fx.DirSparks(_impact, dir, blood, 9, 230f, 0.65f, 3f);
                    g.Fx.ImpactStreak(_impact, dir, Pal.RGBA(255, 236, 230), 26f, 3f, 0.75f, 0.10f);
                }

                // KILL escalation: a kill should feel DECISIVELY bigger than a wound. Layer a
                // brighter/larger burst + a sharper shake + a stronger bloom spike on top of
                // whatever KillUnit does (which fires right after, when D.Hp<=0 below). Reserved
                // for the killing blow only, so ordinary hits stay subtle.
                if (willKill)
                {
                    Color killCol = D.Team == Team.Player ? Pal.Friend : Pal.Foe;
                    g.Fx.Impact(_impact, killCol, Res.Crit ? 34f : 28f, 1f, 0.18f);
                    g.Fx.Flash(_impact, killCol, Res.Crit ? 40f : 34f, 0.18f, 0.62f);   // decisive kill light burst
                    g.Fx.DirSparks(_impact, dir, Pal.RGBA(255, 250, 240), 10, 360f, 0.45f, 3f);
                    g.Fx.ImpactStreak(_impact, dir, killCol, 48f, 5f, 0.95f, 0.13f);
                    g.AddBloom(0.10f);                            // stacks with the hit bloom above
                    g.Fx.AddShake(3.5f);                          // a decisive thump distinct from a wound
                }

                string txt = Res.Crit ? $"CRIT {Res.Damage}" : Res.Damage.ToString();
                // crits + the killing blow get a bigger, brighter number on the impact frame.
                float numSize = willKill ? (Res.Crit ? 36f : 32f) : (Res.Crit ? 32f : 26f);
                Color numCol = Res.Crit ? Pal.Accent : (willKill ? Pal.RGBA(255, 252, 245) : Pal.RGBA(255, 235, 235));
                g.Fx.PopText(D.Pos + new Vector2(0, -26), txt, numCol, numSize);
            }

            if (D.Hp <= 0)
            {
                D.Hp = 0;
                bool wasLastFoe = D.Team == Team.Enemy && g.AliveEnemies().Count <= 1;   // this blow clears the field (D still counts as alive here)
                g.KillUnit(D);
                if (A.Team == Team.Player && D.Team == Team.Enemy)
                {
                    g.CreditKill(A);
                    Audio.PlayStinger(wasLastFoe ? "lastkill" : "kill");   // takedown / field-clear flourish
                }
            }
            else g.MarkPlayerHurt(D);   // a survivor at death's door earns a feat if it lives

            // UNDERTOW W2 — BRACE stagger: a disrupting reaction that connects INTERRUPTS a surviving
            // target — it loses its remaining actions this turn (its post-move shot/grenade is denied,
            // since ActAfterMove gates every action on ActionsLeft>0). The felt comeback lever: trade a
            // kill for tempo. A dead target needs no stagger.
            if (Stagger && D.Alive && !D.Downed)   // FUL-7: a body already down has nothing left to deny
            {
                D.ActionsLeft = 0;
                D.OnOverwatch = false;                       // a rattled unit drops any held reaction too
                // FUL-8 honesty: color the pop by VICTIM team — green was correct for the player's
                // comeback beat, but with the PIKEMAN an enemy brace staggers YOUR soldier; a green
                // pop on your own denied turn is a lie. Foe-red when the victim is yours.
                Color stagCol = D.Team == Team.Player ? Pal.Foe : Pal.Good;
                g.Fx.PopText(D.Pos + new Vector2(0, -30), "STAGGERED", stagCol, 20f);
                g.Fx.Flash(D.Pos, stagCol, 22f, 0.14f, 0.5f);
                g.Fx.AddShake(2.5f);
            }
        }
        else
        {
            // near miss: kick the beam endpoint aside
            // W1: the miss-scatter is PRESENTATION (where the tracer's endpoint lands), so it
            // draws from Util.FxRng, not the gameplay stream. It was event-driven rather than
            // frame-driven, so unlike the shake jitter it never made gameplay frame-dependent —
            // but it is the same class of defect one clutter toggle away from doing so.
            var perp = new Vector2(-dir.Y, dir.X) * Util.FxRandRange(-22f, 22f);
            _impact = D.Pos + perp;
            g.Fx.Burst(_impact, Pal.RGBA(150, 160, 175), 5, 130f, 0.35f, 2f, true);
            // a faint ricochet spit where the round strikes air/terrain (small — it whiffed)
            g.Fx.DirSparks(_impact, dir, Pal.RGBA(170, 180, 195), 4, 150f, 0.9f, 2f);
            g.Fx.PopText(D.Pos + new Vector2(0, -26), "MISS", Pal.TxtDim, 24f);
        }

        // tracer WAKE: drop a few dim, fading glow dots along the beam so the round leaves a brief
        // vapour trail rather than a clean instant beam (deterministic; skipped under AutoPlay).
        if (!_windless)
        {
            Vector2 start = A.Pos + dir * 16f;
            Color wakeCol = Res.Hit ? (Res.Graze ? Pal.RGBA(150, 160, 180) : Pal.Accent) : Pal.RGBA(160, 170, 188);
            g.Fx.TracerWake(start, _impact, Raylib.Fade(wakeCol, 0.6f), 3, 2.3f, 0.13f);
        }

        // combat-log ledger (always-on readability): one terse line per shot with the rolled odds
        // and outcome, so a player can audit a bad miss instead of feeling cheated.
        bool killed = Res.Hit && D.Hp <= 0;
        // FUL-7 ledger honesty: a lethal blow that DOWNED a soldier (Alive, bleeding out) logs
        // "DOWN", not "KILL" — the combat log must never claim a death that hasn't happened.
        string oc = killed ? (D.Alive && D.Downed ? "DOWN" : "KILL") : Res.Crit ? "CRIT" : Res.Graze ? "GRAZE" : Res.Hit ? "HIT" : "MISS";
        string ln = $"{A.Name} > {D.Name}  {oc}" + (Res.Hit ? $" {Res.Damage}" : "") + $"  ({Res.Odds.HitChance}%)";
        Stats.Log(g.Turn, (int)A.Team, ln, oc);
    }

    public override void Draw(Game g)
    {
        float fireAt = FireAt;
        // WIND-UP (anticipation): before the round flies, a small charge-glow swells at the
        // barrel so the muzzle "loads" — reads as tension before release. Skipped when windless.
        if (!_windless && _t < fireAt && fireAt > 0.05f)
        {
            float w = Util.Clamp(_t / fireAt, 0f, 1f);           // 0 -> 1 across the wind-up
            var dir0 = Vector2.Normalize(D.Pos - A.Pos + new Vector2(0.001f, 0f));
            Vector2 mouth = A.Pos + dir0 * 16f;
            float gr = 1.5f + 4.5f * w * w;                      // accelerates as fire nears
            Raylib.DrawCircleV(mouth, gr, Raylib.Fade(Pal.Accent, 0.10f + 0.35f * w));
            Raylib.DrawCircleV(mouth, gr * 0.45f, Raylib.Fade(Pal.RGBA(255, 250, 235), 0.20f + 0.5f * w));
        }
        // recoil nudge handled via facing; draw tracer beam during/after fire
        float beamEnd = BeamEndAt;
        if (_t >= fireAt && _t <= beamEnd)
        {
            float k = 1f - (_t - fireAt) / (beamEnd - fireAt);
            var dir = Vector2.Normalize(D.Pos - A.Pos + new Vector2(0.001f, 0f));
            Vector2 start = A.Pos + dir * 16f;
            // graze fires a dimmer beam than a solid hit (reinforces the lighter "GRAZE" read);
            // a crit's tracer runs a touch hotter/thicker.
            Color beam = Res.Hit ? (Res.Graze ? Pal.RGBA(165, 175, 195) : Pal.Accent) : Pal.RGBA(170, 180, 195);
            float wide = (Res.Hit && Res.Crit) ? 1.35f : 1f;
            // HORIZON W5 — a FATTER, BRIGHTER core so the tracer reliably crosses the bloom
            // bright-pass knee (a volley visibly lights the screen on hardware). Three-layer:
            // a wide outer glow (fades along the beam) -> a bright saturated core -> a hot,
            // now-thicker white centre held at full alpha for the length of the beam so the
            // luma stays above the (lowered) knee end-to-end, not just at the muzzle.
            Raylib.DrawLineEx(start, _impact, (7f * k + 1.2f) * wide, Raylib.Fade(beam, k * 0.34f));
            Raylib.DrawLineEx(start, _impact, (4f * k + 0.8f) * wide, Raylib.Fade(beam, k));
            Raylib.DrawLineEx(start, _impact, (2.2f * k + 0.9f) * wide, Raylib.Fade(Pal.RGBA(255, 255, 255), 0.55f + k * 0.45f));
            // muzzle snap: a quick bright flash-disc at the barrel, biggest at the instant of fire.
            // W5: a bigger, hotter crack (the bloom haloes it) so the shot lands with weight.
            float snap = k * k;       // front-loaded so it cracks then vanishes
            Raylib.DrawCircleV(start, (12f * snap + 2.5f) * wide, Raylib.Fade(Pal.Accent, snap * 0.9f));
            Raylib.DrawCircleV(start, (6f * snap + 1.2f) * wide, Raylib.Fade(Pal.RGBA(255, 250, 235), snap));
        }
    }
}

/// A soldier shoots an explosive barrel: a tracer flies to the barrel tile, then it detonates
/// (Game.DetonateBarrel does the blast/chain/fire). A lightweight cousin of ShotAnim that targets
/// a TILE, not a unit, so the barrel-shot reuses the same muzzle/tracer/audio beat.
public class BarrelShotAnim : Anim
{
    public Unit A;
    public int Tx, Ty;
    Vector2 _to;
    const float Fire = 0.05f, BeamEnd = 0.30f, Total = 0.42f;
    float _t; bool _fired; bool _windless;

    public BarrelShotAnim(Unit a, int tx, int ty) { A = a; Tx = tx; Ty = ty; _to = Util.TileCenter(tx, ty); }

    public override void OnStart(Game g)
    {
        var dir = _to - A.Pos;
        if (dir.LengthSquared() > 0.01f) A.Facing = MathF.Atan2(dir.Y, dir.X);
        _windless = g.AutoPlay;
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_fired && _t >= (_windless ? 0.01f : Fire))
        {
            _fired = true;
            var dir = Vector2.Normalize(_to - A.Pos + new Vector2(0.001f, 0f));
            g.Fx.Muzzle(A.Pos, dir, Pal.Accent);
            Audio.PlayWeapon(A.Weapon.Kind);
            g.SetBarrelCredit(A);          // attribute the chain's kills to the shooter
            g.DetonateBarrel(Tx, Ty);      // boom (+ chain + fire) happens on impact
        }
        return _t >= (_windless ? 0.02f : Total);
    }

    public override void Draw(Game g)
    {
        if (_windless) return;
        if (_t >= Fire && _t <= BeamEnd)
        {
            float k = 1f - (_t - Fire) / (BeamEnd - Fire);
            var dir = Vector2.Normalize(_to - A.Pos + new Vector2(0.001f, 0f));
            Vector2 start = A.Pos + dir * 16f;
            Raylib.DrawLineEx(start, _to, 5.5f * k + 0.8f, Raylib.Fade(Pal.Accent, k * 0.30f));
            Raylib.DrawLineEx(start, _to, 3.2f * k + 0.6f, Raylib.Fade(Pal.Accent, k));
            float snap = k * k;
            Raylib.DrawCircleV(start, 9f * snap + 2f, Raylib.Fade(Pal.Accent, snap * 0.85f));
        }
    }
}

/// A thrown grenade: arcs to a tile, then explodes — AoE damage that ignores
/// cover, hits both teams, and clears low cover. Blast = Chebyshev radius 1.
public class GrenadeAnim : Anim
{
    public Unit Thrower;
    public int Tx, Ty;
    public const int Radius = 1;
    const float Flight = 0.5f;
    const float Total = 0.78f;
    float _t;
    bool _boom;
    Vector2 _from, _to;

    public GrenadeAnim(Unit thrower, int tx, int ty) { Thrower = thrower; Tx = tx; Ty = ty; }

    public override void OnStart(Game g)
    {
        _from = Thrower.Pos;
        _to = Util.TileCenter(Tx, Ty);
        var d = _to - _from;
        if (d.LengthSquared() > 0.01f) Thrower.Facing = MathF.Atan2(d.Y, d.X);
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_boom && _t >= Flight) { _boom = true; Explode(g); }
        return _t >= Total;
    }

    void Explode(Game g)
    {
        float pan = Util.Clamp(_to.X / (float)Cfg.ScreenW, 0f, 1f);
        Audio.Play(Audio.CueFor(Audio.GameEvent.Explosion), 0f, pan);   // THE BEAT: a real explosion cue (was "crit"+"death" stacked)
        // a brief additive flash of LIGHT at the detonation core (the bloom haloes it)
        g.Fx.Flash(_to, Pal.RGBA(255, 226, 180), (Radius + 0.5f) * Cfg.Tile * 0.5f, 0.16f, 0.6f);
        g.Fx.AddShake(12f);
        g.AddHitStop(0.07f);
        g.AddZoomPunch(0.06f);
        g.Fx.Burst(_to, Pal.Accent, 36, 360f, 0.6f, 4.5f, true);
        g.Fx.Burst(_to, Pal.RGBA(120, 90, 60), 22, 200f, 0.8f, 5f);

        // expanding shockwave sized to the blast radius + a bright impact flash at the core
        float blastR = (Radius + 0.5f) * Cfg.Tile;
        g.Fx.Shockwave(_to, Pal.RGBA(255, 226, 180), 10f, blastR, 5f, 0.95f, 0.30f);
        g.Fx.Impact(_to, Pal.Accent, blastR * 0.42f, 0.9f, 0.14f);
        // dirt/debris flung outward (a chunkier, slower-fading complement to the spark burst)
        g.Fx.DirSparks(_to, new Vector2(1f, -0.3f), Pal.RGBA(150, 120, 84), 14, 240f, MathF.PI, 3.5f);

        // chew up cover in the blast: a frag cracks high->low and clears low cover (3.6)
        for (int x = Tx - Radius; x <= Tx + Radius; x++)
            for (int y = Ty - Radius; y <= Ty + Radius; y++)
            {
                if (!g.Grid.InBounds(x, y)) continue;
                var ch = g.Grid.DamageCover(x, y, Grid.HighCoverHp);   // 2 dmg: high->low, low->gone
                if (ch != Grid.CoverHit.None)
                {
                    g.CoverHitFx(x, y, ch);
                    // shattered cover throws extra debris from its tile
                    g.Fx.Burst(Util.TileCenter(x, y), Pal.RGBA(140, 116, 86), 10, 180f, 0.7f, 4f);
                }
            }

        // damage every unit in radius (friendly fire included)
        var hit = new System.Collections.Generic.List<Unit>();
        hit.AddRange(g.Players);
        hit.AddRange(g.Enemies);
        var wokePods = new System.Collections.Generic.HashSet<int>();
        foreach (var u in hit)
        {
            if (!u.Alive) continue;
            if (u == g.Vip && g.CaptiveLocked) continue;   // the caged captive is invulnerable
            if (Util.ChebyDist(u.X, u.Y, Tx, Ty) > Radius) continue;
            if (u.Team == Team.Enemy && !u.Active) wokePods.Add(u.PodId);
            int dmg = Util.RandInt(3, 5);
            dmg = Combat.HardenedReduce(u, dmg, crit: false);   // tank: -1 (a blast can't "crit")
            // fragile-unit floor (mirrors Combat.Resolve): a FULL-HP player/VIP can't be deleted
            // from full by a single blast -- it's left at 1 HP. Softens the worst grenade feel-bad
            // (losing a soldier/VIP from full to one frag); enemies are not protected.
            if (u.Team == Team.Player && u.MaxHp >= 2 && u.Hp >= u.MaxHp) dmg = Math.Min(dmg, u.MaxHp - 1);
            u.Hp -= dmg;
            u.Flash = 1f;
            u.FlinchAnim = 1f;       // the blast rocks them (Renderer hit-flinch); decays in Game.Update
            var kick = u.Pos - _to;
            if (kick.LengthSquared() > 0.01f) u.Recoil = Vector2.Normalize(kick) * 7f;
            Color c = u.Team == Team.Player ? Pal.Friend : Pal.Foe;
            g.Fx.Burst(u.Pos, c, 12, 200f, 0.5f, 3.5f, true);
            g.Fx.PopText(u.Pos + new Vector2(0, -26), dmg.ToString(), Pal.RGBA(255, 220, 180), 26f);
            if (u.Hp <= 0)
            {
                u.Hp = 0;
                bool wasLastFoe = u.Team == Team.Enemy && g.AliveEnemies().Count <= 1;
                g.KillUnit(u);
                if (Thrower.Team == Team.Player && u.Team == Team.Enemy)
                {
                    g.CreditKill(Thrower);
                    Audio.PlayStinger(wasLastFoe ? "lastkill" : "kill");
                }
            }
            else { g.MarkPlayerHurt(u); u.AddStatus(StatusKind.Burning, 2); }   // blast leaves them on fire
        }
        foreach (int pod in wokePods) g.ActivatePod(pod);   // the blast wakes survivors

        // chain-detonate any explosive barrel caught in the frag (a grenade near a barrel cooks it
        // off). Credit the chain's kills to the thrower; collect first so we don't mutate mid-scan.
        var barrels = new System.Collections.Generic.List<(int x, int y)>();
        for (int x = Tx - Radius; x <= Tx + Radius; x++)
            for (int y = Ty - Radius; y <= Ty + Radius; y++)
                if (g.Grid.IsBarrel(x, y)) barrels.Add((x, y));
        if (barrels.Count > 0)
        {
            g.SetBarrelCredit(Thrower);
            foreach (var (x, y) in barrels) g.DetonateBarrel(x, y);
        }
    }

    public override void Draw(Game g)
    {
        if (_t < Flight)
        {
            float k = _t / Flight;
            Vector2 p = ArcAt(k);
            // fading arc TRAIL: sample a few earlier points along the parabola so the thrown
            // ordnance reads as motion (deterministic — pure function of _t). Drawn behind the head.
            DrawArcTrail(k, Pal.Accent);
            Raylib.DrawCircleV(p + new Vector2(2, 3), 5f, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.4f));
            Raylib.DrawCircleV(p, 5f, Pal.Accent);
            Raylib.DrawCircleV(p, 2.5f, Pal.RGBA(255, 240, 200));
        }
        else
        {
            float k = (_t - Flight) / (Total - Flight);
            float rad = Util.Lerp(8f, (Radius + 0.5f) * Cfg.Tile, Util.EaseOutQuad(k));
            Raylib.DrawCircleV(_to, rad, Raylib.Fade(Pal.Accent, (1f - k) * 0.4f));
            Raylib.DrawRing(_to, rad - 4, rad, 0, 360, 40, Raylib.Fade(Pal.RGBA(255, 230, 190), 1f - k));
        }
    }

    // position on the parabolic arc at normalised flight progress k in [0,1].
    Vector2 ArcAt(float k)
    {
        Vector2 p = Vector2.Lerp(_from, _to, k);
        p.Y -= MathF.Sin(k * MathF.PI) * 70f;
        return p;
    }

    // a short dimming poly-line of the last ~6 arc samples behind the grenade head.
    void DrawArcTrail(float k, Color col)
    {
        const int N = 6;
        Vector2 prev = ArcAt(k);
        for (int i = 1; i <= N; i++)
        {
            float kk = k - i * 0.018f;
            if (kk < 0f) break;
            Vector2 cur = ArcAt(kk);
            float a = 0.34f * (1f - i / (float)(N + 1));
            Raylib.DrawLineEx(prev, cur, MathF.Max(1f, 3.2f * (1f - i / (float)(N + 2))),
                              Raylib.Fade(col, a));
            prev = cur;
        }
    }
}

/// Base for lobbed utility throwables (smoke / flash): parabolic arc, then a
/// one-shot Effect on landing. Mirrors the grenade arc, tinted per item.
public abstract class LobAnim : Anim
{
    protected readonly Unit Thrower;
    protected readonly int Tx, Ty;
    protected Color Tint = Pal.Accent;
    const float Flight = 0.5f;
    const float Total = 0.72f;
    float _t;
    bool _boom;
    Vector2 _from, _to;

    protected LobAnim(Unit thrower, int tx, int ty) { Thrower = thrower; Tx = tx; Ty = ty; }
    protected virtual int BlastRadius => 1;

    public override void OnStart(Game g)
    {
        _from = Thrower.Pos;
        _to = Util.TileCenter(Tx, Ty);
        var d = _to - _from;
        if (d.LengthSquared() > 0.01f) Thrower.Facing = MathF.Atan2(d.Y, d.X);
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_boom && _t >= Flight) { _boom = true; Effect(g); }
        return _t >= Total;
    }

    protected abstract void Effect(Game g);

    public override void Draw(Game g)
    {
        if (_t < Flight)
        {
            float k = _t / Flight;
            Vector2 p = ArcAt(k);
            DrawArcTrail(k, Tint);   // fading motion trail behind the thrown item (deterministic)
            Raylib.DrawCircleV(p + new Vector2(2, 3), 5f, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.4f));
            Raylib.DrawCircleV(p, 5f, Tint);
            Raylib.DrawCircleV(p, 2.5f, Pal.RGBA(245, 245, 245));
        }
        else
        {
            float k = (_t - Flight) / (Total - Flight);
            float rad = Util.Lerp(8f, (BlastRadius + 0.5f) * Cfg.Tile, Util.EaseOutQuad(k));
            Raylib.DrawCircleV(_to, rad, Raylib.Fade(Tint, (1f - k) * 0.4f));
        }
    }

    Vector2 ArcAt(float k)
    {
        Vector2 p = Vector2.Lerp(_from, _to, k);
        p.Y -= MathF.Sin(k * MathF.PI) * 64f;
        return p;
    }

    void DrawArcTrail(float k, Color col)
    {
        const int N = 6;
        Vector2 prev = ArcAt(k);
        for (int i = 1; i <= N; i++)
        {
            float kk = k - i * 0.018f;
            if (kk < 0f) break;
            Vector2 cur = ArcAt(kk);
            float a = 0.30f * (1f - i / (float)(N + 1));
            Raylib.DrawLineEx(prev, cur, MathF.Max(1f, 3f * (1f - i / (float)(N + 2))),
                              Raylib.Fade(col, a));
            prev = cur;
        }
    }
}

/// Smoke grenade: lays a sight-blocking cloud over a 3x3 area for a few turns.
public class SmokeAnim : LobAnim
{
    public const int Radius = 1;
    public const int Turns = 3;        // player turns the cloud lingers
    public SmokeAnim(Unit thrower, int tx, int ty) : base(thrower, tx, ty) { Tint = Pal.RGBA(150, 158, 168); }
    protected override int BlastRadius => Radius;

    protected override void Effect(Game g)
    {
        // THE BEAT: a soft hiss (was the "hunker" thunk). P14: PANNED to the canister — the
        // GrenadeAnim two classes up has always panned its blast and these three siblings did not,
        // so a smoke landing at the board edge hissed dead centre.
        Audio.Play("smoke", panX: Util.Clamp(Util.TileCenter(Tx, Ty).X / (float)Cfg.ScreenW, 0f, 1f));
        g.Grid.AddSmoke(Tx, Ty, Radius, Turns);
        for (int x = Tx - Radius; x <= Tx + Radius; x++)
            for (int y = Ty - Radius; y <= Ty + Radius; y++)
                if (g.Grid.InBounds(x, y))
                    g.Fx.Burst(Util.TileCenter(x, y), Pal.RGBA(160, 168, 178), 9, 70f, 1.1f, 6f);
    }
}

/// Flashbang: AoE that disorients everyone caught in the blast (both teams) and
/// denies their held overwatch. Reuses the status system (3.5).
public class FlashAnim : LobAnim
{
    public const int Radius = 1;
    public const int DisorientTurns = 2;
    public FlashAnim(Unit thrower, int tx, int ty) : base(thrower, tx, ty) { Tint = Pal.RGBA(255, 250, 230); }
    protected override int BlastRadius => Radius;

    protected override void Effect(Game g)
    {
        // THE BEAT: a flashbang pings, it does not "crit". P14: panned to the burst.
        Audio.Play("flash", panX: Util.Clamp(Util.TileCenter(Tx, Ty).X / (float)Cfg.ScreenW, 0f, 1f));
        g.Fx.AddShake(7f);
        g.AddHitStop(0.04f);
        g.Fx.Burst(Util.TileCenter(Tx, Ty), Pal.RGBA(255, 250, 230), 34, 320f, 0.4f, 5f, true);

        var all = new System.Collections.Generic.List<Unit>();
        all.AddRange(g.Players); all.AddRange(g.Enemies);
        var wokePods = new System.Collections.Generic.HashSet<int>();
        foreach (var u in all)
        {
            if (!u.Alive || Util.ChebyDist(u.X, u.Y, Tx, Ty) > Radius) continue;
            if (u.Team == Team.Enemy && !u.Active) wokePods.Add(u.PodId);
            u.AddStatus(StatusKind.Disoriented, DisorientTurns);
            u.OnOverwatch = false;             // the flash breaks any held overwatch
            u.Flash = 1f;
            g.Fx.PopText(u.Pos + new Vector2(0, -26), "DAZED", Pal.RGBA(255, 240, 200), 20f);
        }
        foreach (int pod in wokePods) g.ActivatePod(pod);
    }
}

/// Incendiary: lobs a fire bomb that lays a 3x3 fire field (deny ground / ignite / cook
/// barrels). The player's agency over the Wave-2 hazard system — reuses Grid.AddFire +
/// the barrel-cook path so it composes with everything fire already does.
public class IncendiaryAnim : LobAnim
{
    public const int Radius = 1;
    public IncendiaryAnim(Unit thrower, int tx, int ty) : base(thrower, tx, ty) { Tint = Pal.RGBA(255, 150, 60); }
    protected override int BlastRadius => Radius;

    protected override void Effect(Game g)
    {
        // THE BEAT: a smaller explosion (was "crit"). P14: panned, and through Audio.Cue like
        // every other Explosion site — the gain trim rides along.
        Audio.Cue(Audio.GameEvent.Explosion,
                  panX: Util.Clamp(Util.TileCenter(Tx, Ty).X / (float)Cfg.ScreenW, 0f, 1f), gainDb: -6f);
        g.Fx.AddShake(6f);
        g.Fx.Burst(Util.TileCenter(Tx, Ty), Pal.RGBA(255, 160, 70), 30, 300f, 0.5f, 5f, true);
        // lay the fire field. W10 PYROMANIACS boon: a SQUAD-thrown incendiary burns +2 turns
        // (team-gated so a hostile fire-starter never inherits the player's boon).
        // FUL-1 PROC: once per squad-thrown incendiary whose field got the extension
        if (Thrower != null && Thrower.Team == Team.Player && g.HasBoon(Sightline.Boon.Pyromaniacs))
            Stats.RecordProc("PYR");
        g.Grid.AddFire(Tx, Ty, Radius, Grid.FireTurns
            + (Thrower != null && Thrower.Team == Team.Player && g.HasBoon(Sightline.Boon.Pyromaniacs) ? 2 : 0));

        // ignite + sear any unit caught in the initial burst (both teams); the lingering Fire
        // field then handles step-in / standing damage via the normal hazard tick.
        foreach (var u in g.Players.Concat(g.Enemies).ToList())
        {
            if (!u.Alive || Util.ChebyDist(u.X, u.Y, Tx, Ty) > Radius) continue;
            if (u == g.Vip && g.CaptiveLocked) continue;
            u.AddStatus(StatusKind.Burning, 2);
        }
        // cook off any barrel caught in the blast (credit the thrower)
        var barrels = new System.Collections.Generic.List<(int x, int y)>();
        for (int x = Tx - Radius; x <= Tx + Radius; x++)
            for (int y = Ty - Radius; y <= Ty + Radius; y++)
                if (g.Grid.IsBarrel(x, y)) barrels.Add((x, y));
        if (barrels.Count > 0)
        {
            g.SetBarrelCredit(Thrower);
            foreach (var (x, y) in barrels) g.DetonateBarrel(x, y);
        }
    }
}

/// A medic mends an ally: a green link + particle burst, then restores HP.
public class HealAnim : Anim
{
    public Unit Medic, Patient;
    const float Apply = 0.18f;
    const float Total = 0.5f;
    float _t;
    bool _done;

    public HealAnim(Unit medic, Unit patient) { Medic = medic; Patient = patient; }

    public override void OnStart(Game g)
    {
        var d = Patient.Pos - Medic.Pos;
        if (d.LengthSquared() > 0.01f) Medic.Facing = MathF.Atan2(d.Y, d.X);
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_done && _t >= Apply)
        {
            _done = true;
            if (Patient.Alive && Patient.Hp < Patient.MaxHp)
            {
                int before = Patient.Hp;
                Patient.Hp = Math.Min(Patient.MaxHp, Patient.Hp + Ai.HealAmount);
                int gained = Patient.Hp - before;
                g.Fx.Burst(Patient.Pos, Pal.Good, 14, 150f, 0.6f, 3.5f, true);
                g.Fx.PopText(Patient.Pos + new Vector2(0, -26), "+" + gained, Pal.Good, 24f);
                // THE BEAT: a mend sounds like a mend (was "reload"). P14: panned to the patient.
                Audio.Cue(Audio.GameEvent.Mend,
                          panX: Util.Clamp(Patient.Pos.X / (float)Cfg.ScreenW, 0f, 1f));
            }
        }
        return _t >= Total;
    }

    public override void Draw(Game g)
    {
        float k = 1f - Util.Clamp(_t / (Apply + 0.18f), 0f, 1f);
        if (k > 0f)
            Raylib.DrawLineEx(Medic.Pos, Patient.Pos, 2.5f, Raylib.Fade(Pal.Good, 0.2f + 0.6f * k));
    }
}

/// A brief pause (used to space out AI actions so they read clearly).
public class WaitAnim : Anim
{
    float _t, _dur;
    public WaitAnim(float dur) { _dur = dur; }
    public override bool Update(Game g, float dt) { _t += dt; return _t >= _dur; }
}
