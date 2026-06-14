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

/// A fired shot: recoil, muzzle flash, tracer beam, then resolve damage.
public class ShotAnim : Anim
{
    public Unit A, D;
    public ShotResult Res;
    public bool Reaction;

    const float Fire = 0.14f;
    const float BeamEnd = 0.34f;
    const float Total = 0.52f;
    float _t;
    bool _applied;
    Vector2 _impact;

    public ShotAnim(Unit a, Unit d, ShotResult res, bool reaction = false)
    { A = a; D = d; Res = res; Reaction = reaction; }

    public override void OnStart(Game g)
    {
        var dir = D.Pos - A.Pos;
        if (dir.LengthSquared() > 0.01f) A.Facing = MathF.Atan2(dir.Y, dir.X);
        _impact = D.Pos;
    }

    public override bool Update(Game g, float dt)
    {
        _t += dt;
        if (!_applied && _t >= Fire)
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
        g.Fx.AddShake(Res.Hit ? (Res.Crit ? 9f : 5f) : 2.5f);
        Audio.Play("shoot");
        Audio.Play(Res.Hit ? (Res.Crit ? "crit" : "hit") : "miss");
        A.Recoil = -dir * (A.Weapon.Kind == WeaponKind.Shotgun ? 9f : 6f); // kick back

        if (Res.Hit)
        {
            _impact = D.Pos;
            D.Hp -= Res.Damage;
            D.Flash = 1f;
            D.Recoil = dir * (Res.Crit ? 8f : 5f);           // knockback
            g.AddHitStop(Res.Crit ? 0.09f : 0.05f);          // freeze on impact
            Color blood = D.Team == Team.Player ? Pal.Friend : Pal.Foe;
            g.Fx.Burst(D.Pos, blood, Res.Crit ? 22 : 13, Res.Crit ? 320f : 200f, 0.5f, 3.5f, true);
            g.Fx.Burst(D.Pos, Pal.RGBA(230, 230, 235), 6, 120f, 0.4f, 2.5f);

            string txt = Res.Crit ? $"CRIT {Res.Damage}" : Res.Damage.ToString();
            g.Fx.PopText(D.Pos + new Vector2(0, -26), txt, Res.Crit ? Pal.Accent : Pal.RGBA(255, 235, 235),
                         Res.Crit ? 32f : 26f);

            if (D.Hp <= 0)
            {
                D.Hp = 0;
                g.KillUnit(D);
                if (A.Team == Team.Player && D.Team == Team.Enemy) A.Kills++;
            }
        }
        else
        {
            // near miss: kick the beam endpoint aside
            var perp = new Vector2(-dir.Y, dir.X) * Util.RandRange(-22f, 22f);
            _impact = D.Pos + perp;
            g.Fx.Burst(_impact, Pal.RGBA(150, 160, 175), 5, 130f, 0.35f, 2f, true);
            g.Fx.PopText(D.Pos + new Vector2(0, -26), "MISS", Pal.TxtDim, 24f);
        }
    }

    public override void Draw(Game g)
    {
        // recoil nudge handled via facing; draw tracer beam during/after fire
        if (_t >= Fire && _t <= BeamEnd)
        {
            float k = 1f - (_t - Fire) / (BeamEnd - Fire);
            var dir = Vector2.Normalize(D.Pos - A.Pos + new Vector2(0.001f, 0f));
            Vector2 start = A.Pos + dir * 16f;
            Color beam = Res.Hit ? Pal.Accent : Pal.RGBA(170, 180, 195);
            Raylib.DrawLineEx(start, _impact, 3.5f * k + 0.6f, Raylib.Fade(beam, k));
            Raylib.DrawLineEx(start, _impact, 1.2f, Raylib.Fade(Pal.RGBA(255, 255, 255), k * 0.8f));
            // muzzle glow
            Raylib.DrawCircleV(start, 7f * k, Raylib.Fade(Pal.Accent, k * 0.8f));
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

        // clear low cover in the blast (debris)
        for (int x = Tx - Radius; x <= Tx + Radius; x++)
            for (int y = Ty - Radius; y <= Ty + Radius; y++)
            {
                if (!g.Grid.InBounds(x, y)) continue;
                if (g.Grid.Tiles[x, y] == TileType.LowCover)
                {
                    g.Grid.Tiles[x, y] = TileType.Floor;
                    g.Fx.Burst(Util.TileCenter(x, y), Pal.RGBA(90, 100, 116), 10, 150f, 0.6f, 4f);
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
            if (Util.ChebyDist(u.X, u.Y, Tx, Ty) > Radius) continue;
            if (u.Team == Team.Enemy && !u.Active) wokePods.Add(u.PodId);
            int dmg = Util.RandInt(3, 5);
            u.Hp -= dmg;
            u.Flash = 1f;
            var kick = u.Pos - _to;
            if (kick.LengthSquared() > 0.01f) u.Recoil = Vector2.Normalize(kick) * 7f;
            Color c = u.Team == Team.Player ? Pal.Friend : Pal.Foe;
            g.Fx.Burst(u.Pos, c, 12, 200f, 0.5f, 3.5f, true);
            g.Fx.PopText(u.Pos + new Vector2(0, -26), dmg.ToString(), Pal.RGBA(255, 220, 180), 26f);
            if (u.Hp <= 0)
            {
                u.Hp = 0;
                g.KillUnit(u);
                if (Thrower.Team == Team.Player && u.Team == Team.Enemy) Thrower.Kills++;
            }
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

/// A brief pause (used to space out AI actions so they read clearly).
public class WaitAnim : Anim
{
    float _t, _dur;
    public WaitAnim(float dur) { _dur = dur; }
    public override bool Update(Game g, float dt) { _t += dt; return _t >= _dur; }
}
