using System;
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

/// Move a single unit one tile along its path; checks overwatch on arrival.
public class MoveStepAnim : Anim
{
    public Unit Unit;
    public int Tx, Ty;
    Vector2 _from, _to;
    float _t, _dur;

    public MoveStepAnim(Unit u, int tx, int ty) { Unit = u; Tx = tx; Ty = ty; }

    public override void OnStart(Game g)
    {
        _from = Unit.Pos;
        _to = Util.TileCenter(Tx, Ty);
        bool diag = Tx != Unit.X && Ty != Unit.Y;
        _dur = diag ? 0.155f : 0.12f;
        var d = _to - _from;
        if (d.LengthSquared() > 0.01f) Unit.Facing = MathF.Atan2(d.Y, d.X);
        Unit.WalkLean = 1f;     // lean into the step (Renderer reads it as a forward body tilt); decays in Game.Update
        g.Fx.Dust(_from + new Vector2(0, 8f), 3);   // a small puff kicks up as the foot leaves
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        float k = Util.Clamp(_t / _dur, 0f, 1f);
        Unit.Pos = Vector2.Lerp(_from, _to, Util.EaseInOutQuad(k));
        if (k >= 1f)
        {
            Unit.X = Tx; Unit.Y = Ty;
            Unit.Pos = _to;
            g.OnUnitEnteredTile(Unit);
            return true;
        }
        return false;
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
            var rammed = g.UnitAt(_tx, _ty);   // null if blocked by terrain/edge rather than a unit
            g.EnvDamage(Target, Math.Max(1, Combat.ShoveCollisionDamage), "SLAM", Pal.RGBA(255, 210, 150));
            if (rammed != null && rammed.Alive && rammed != Target)
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

    // A shot reads as a 3-beat: a brief WIND-UP (anticipation — reticle snaps in, muzzle
    // charges) -> FIRE (muzzle/tracer/impact land) -> SETTLE. The wind-up is kept short so
    // play doesn't drag; under AutoPlay it's skipped (Anim Total is unchanged either way).
    const float WindUp = 0.10f;
    const float Fire = WindUp + 0.04f;   // muzzle/tracer/Apply fire just after the wind-up
    const float BeamEnd = 0.34f;
    const float Total = 0.52f;
    float _t;
    bool _applied;
    Vector2 _impact;
    bool _windless;        // AutoPlay: collapse the wind-up so the smoke/balance harness stays fast

    public ShotAnim(Unit a, Unit d, ShotResult res, bool reaction = false)
    { A = a; D = d; Res = res; Reaction = reaction; }

    // effective fire time — under AutoPlay the wind-up anticipation is dropped so headless
    // runs don't slow (the visual beat is purely for a human watching).
    float FireAt => _windless ? 0.04f : Fire;

    public override void OnStart(Game g)
    {
        var dir = D.Pos - A.Pos;
        if (dir.LengthSquared() > 0.01f) A.Facing = MathF.Atan2(dir.Y, dir.X);
        _impact = D.Pos;
        _windless = g.AutoPlay;
        if (!_windless)
        {
            // anticipation: a reticle snaps onto the target over the wind-up beat, tinted to
            // the firer's team, so the eye is drawn to the impact point before the round flies.
            Color ret = A.Team == Team.Player ? Pal.Friend : Pal.Foe;
            g.Fx.ReticleSnap(D.Pos, ret, 26f, 13f, 0.7f, WindUp + 0.02f);
        }
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_applied && _t >= FireAt)
        {
            _applied = true;
            Apply(g);
        }
        return _t >= Total;
    }

    void Apply(Game g)
    {
        var dir = Vector2.Normalize(D.Pos - A.Pos + new Vector2(0.001f, 0f));
        Color muzzleCol = A.Team == Team.Player ? Pal.Friend : Pal.Foe;
        g.Fx.Muzzle(A.Pos, dir, Pal.Accent);
        // Graze shakes less than a solid hit.
        g.Fx.AddShake(Res.Hit ? (Res.Graze ? 2f : (Res.Crit ? 9f : 5f)) : 2.5f);
        Audio.PlayWeapon(A.Weapon.Kind);   // per-weapon firing voice (rifle/shotgun/sniper/lmg/smg)
        Audio.Play(Res.Hit ? (Res.Crit ? "crit" : "hit") : "miss");
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
                    g.Fx.DirSparks(_impact, dir, Pal.Accent, 16, 360f, 0.5f, 3.4f);
                    g.Fx.DirSparks(_impact, dir, Pal.RGBA(255, 250, 240), 6, 280f, 0.35f, 2.6f);
                    g.Fx.ImpactStreak(_impact, dir, Pal.Accent, 40f, 4.2f, 0.9f, 0.12f);
                    g.Fx.AddShake(1.5f);                          // a sharper extra kick on a crit
                }
                else
                {
                    g.Fx.Impact(_impact, Pal.RGBA(255, 240, 235), 16f, 0.85f, 0.12f);
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
        }
        else
        {
            // near miss: kick the beam endpoint aside
            var perp = new Vector2(-dir.Y, dir.X) * Util.RandRange(-22f, 22f);
            _impact = D.Pos + perp;
            g.Fx.Burst(_impact, Pal.RGBA(150, 160, 175), 5, 130f, 0.35f, 2f, true);
            // a faint ricochet spit where the round strikes air/terrain (small — it whiffed)
            g.Fx.DirSparks(_impact, dir, Pal.RGBA(170, 180, 195), 4, 150f, 0.9f, 2f);
            g.Fx.PopText(D.Pos + new Vector2(0, -26), "MISS", Pal.TxtDim, 24f);
        }

        // combat-log ledger (always-on readability): one terse line per shot with the rolled odds
        // and outcome, so a player can audit a bad miss instead of feeling cheated.
        bool killed = Res.Hit && D.Hp <= 0;
        string oc = killed ? "KILL" : Res.Crit ? "CRIT" : Res.Graze ? "GRAZE" : Res.Hit ? "HIT" : "MISS";
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
        if (_t >= fireAt && _t <= BeamEnd)
        {
            float k = 1f - (_t - fireAt) / (BeamEnd - fireAt);
            var dir = Vector2.Normalize(D.Pos - A.Pos + new Vector2(0.001f, 0f));
            Vector2 start = A.Pos + dir * 16f;
            // graze fires a dimmer beam than a solid hit (reinforces the lighter "GRAZE" read);
            // a crit's tracer runs a touch hotter/thicker.
            Color beam = Res.Hit ? (Res.Graze ? Pal.RGBA(165, 175, 195) : Pal.Accent) : Pal.RGBA(170, 180, 195);
            float wide = (Res.Hit && Res.Crit) ? 1.25f : 1f;
            // outer glow trail (fades along the beam) -> bright core -> hot white center
            Raylib.DrawLineEx(start, _impact, (5.5f * k + 0.8f) * wide, Raylib.Fade(beam, k * 0.30f));
            Raylib.DrawLineEx(start, _impact, (3.2f * k + 0.6f) * wide, Raylib.Fade(beam, k));
            Raylib.DrawLineEx(start, _impact, 1.3f * wide, Raylib.Fade(Pal.RGBA(255, 255, 255), k * 0.9f));
            // muzzle snap: a quick bright flash-disc at the barrel, biggest at the instant of fire
            float snap = k * k;       // front-loaded so it cracks then vanishes
            Raylib.DrawCircleV(start, (9f * snap + 2f) * wide, Raylib.Fade(Pal.Accent, snap * 0.85f));
            Raylib.DrawCircleV(start, (4.5f * snap + 1f) * wide, Raylib.Fade(Pal.RGBA(255, 250, 235), snap));
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
        Audio.Play("crit");
        Audio.Play("death");
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
    }

    public override void Draw(Game g)
    {
        if (_t < Flight)
        {
            float k = _t / Flight;
            Vector2 p = Vector2.Lerp(_from, _to, k);
            p.Y -= MathF.Sin(k * MathF.PI) * 70f;          // parabolic arc
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
            Vector2 p = Vector2.Lerp(_from, _to, k);
            p.Y -= MathF.Sin(k * MathF.PI) * 64f;          // parabolic arc
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
        Audio.Play("hunker");
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
        Audio.Play("crit");
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
                Audio.Play("reload");
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
