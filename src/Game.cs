using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Breach;

public enum Phase { Intro, PlayerTurn, EnemyTurn, Win, Lose }
enum AiStage { PickNext, ActAfterMove }

public class Game
{
    public Grid Grid = new();
    public List<Unit> Players = new();
    public List<Unit> Enemies = new();
    public Fx Fx = new();

    public Phase Phase = Phase.Intro;
    public Team AnimOwner = Team.Player;

    readonly List<Anim> _anims = new();
    public Anim ActiveAnim => _anims.Count > 0 ? _anims[0] : null;

    // selection / hover
    public Unit Selected;
    public int HoverX, HoverY;
    public bool HoverValid;
    public int[,] MoveCost;
    (int, int)[,] _cameFrom;
    public List<(int x, int y)> PathPreview = new();

    // aiming
    public bool AimMode;
    public Unit AimTarget;
    public bool AimValid;
    public bool ShowOdds;
    public ShotOdds HoverOdds;

    // banner
    public string BannerText = "";
    public float BannerTimer, BannerMax;
    public bool BannerEnemy;

    // ai staging
    AiStage _aiStage;
    List<Unit> _aiUnits = new();
    int _aiIdx;
    EnemyPlan _aiPlan;

    int _turnCount;

    // ---------------- lifecycle ----------------
    public void StartMission()
    {
        Mission.Build(Grid, Players, Enemies);
        Fx.Particles.Clear();
        Fx.Texts.Clear();
        _anims.Clear();
        _turnCount = 1;
        Phase = Phase.PlayerTurn;
        foreach (var u in Players) u.BeginTurn();
        foreach (var u in Enemies) { u.BeginTurn(); u.OnOverwatch = false; }
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        ShowBanner("PLAYER TURN", false);
    }

    void ShowBanner(string text, bool enemy)
    {
        BannerText = text; BannerEnemy = enemy;
        BannerMax = BannerTimer = 1.2f;
    }

    // ---------------- queries ----------------
    public List<Unit> AlivePlayers() => Players.Where(u => u.Alive).ToList();
    public List<Unit> AliveEnemies() => Enemies.Where(u => u.Alive).ToList();

    public bool IsPlayerInteractive() => Phase == Phase.PlayerTurn && _anims.Count == 0;

    public bool IsOccupiedByOther(int x, int y, Unit except)
    {
        foreach (var u in Players) if (u.Alive && u != except && u.X == x && u.Y == y) return true;
        foreach (var u in Enemies) if (u.Alive && u != except && u.X == x && u.Y == y) return true;
        return false;
    }

    public Unit UnitAt(int x, int y)
    {
        foreach (var u in Players) if (u.Alive && u.X == x && u.Y == y) return u;
        foreach (var u in Enemies) if (u.Alive && u.X == x && u.Y == y) return u;
        return null;
    }

    public bool CanTarget(Unit a, Unit d)
    {
        if (a == null || d == null || !a.Alive || !d.Alive || a.Ammo <= 0) return false;
        if (a.Team == d.Team) return false;
        if (Util.TileDist(a.X, a.Y, d.X, d.Y) > a.Weapon.MaxRange) return false;
        return Grid.HasLineOfSight(a.X, a.Y, d.X, d.Y);
    }

    public bool HasAnyTarget(Unit u)
    {
        var foes = u.Team == Team.Player ? Enemies : Players;
        return foes.Any(f => f.Alive && CanTarget(u, f));
    }

    Unit FirstTargetFor(Unit u)
    {
        var foes = (u.Team == Team.Player ? Enemies : Players).Where(f => f.Alive && CanTarget(u, f));
        return foes.OrderBy(f => Util.TileDist(u.X, u.Y, f.X, f.Y)).FirstOrDefault();
    }

    void Enqueue(Anim a, Team owner) { AnimOwner = owner; a.OnStart(this); _anims.Add(a); }

    // ---------------- combat events ----------------
    public void OnUnitEnteredTile(Unit mover)
    {
        if (!mover.Alive) return;
        var watchers = mover.Team == Team.Player ? Enemies : Players;
        int insertAt = 1;
        foreach (var w in watchers)
        {
            if (!w.Alive || !w.OnOverwatch || w.ReactedThisTurn || w.Ammo <= 0) continue;
            if (!CanTarget(w, mover)) continue;
            w.OnOverwatch = false;
            w.ReactedThisTurn = true;
            w.Ammo--;
            var res = Combat.Resolve(Grid, w, mover, -10); // reaction penalty
            Fx.PopText(w.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
            var shot = new ShotAnim(w, mover, res, reaction: true);
            shot.OnStart(this);
            _anims.Insert(Math.Min(insertAt, _anims.Count), shot);
            insertAt++;
            if (res.Hit && mover.Hp - res.Damage <= 0) break; // will die; stop further reactions
        }
    }

    public void KillUnit(Unit d)
    {
        d.Alive = false;
        d.Hp = 0;
        Color c = d.Team == Team.Player ? Pal.Friend : Pal.Foe;
        Fx.Burst(d.Pos, c, 30, 280f, 0.7f, 4f, true);
        Fx.Burst(d.Pos, Pal.RGBA(20, 25, 33), 16, 150f, 0.8f, 5f);
        Fx.PopText(d.Pos + new Vector2(0, -10), "DOWN", c, 22f);
        Fx.AddShake(7f);
        // purge any queued movement for the dead unit
        _anims.RemoveAll(a => a is MoveStepAnim m && m.Unit == d);
        if (Selected == d) Selected = null;
    }

    // ---------------- update ----------------
    public void Update(float dt)
    {
        float t = MathF.Min(dt, 0.05f);
        Fx.Update(t);
        foreach (var u in Players) u.Flash = MathF.Max(0, u.Flash - t * 4f);
        foreach (var u in Enemies) u.Flash = MathF.Max(0, u.Flash - t * 4f);
        if (BannerTimer > 0) BannerTimer -= t;

        // advance animation queue
        if (_anims.Count > 0)
        {
            var a = _anims[0];
            if (!a.Started) { a.Started = true; }
            if (a.Update(this, t)) _anims.RemoveAt(0);
            return;
        }

        switch (Phase)
        {
            case Phase.Intro: HandleOverlayClick(); break;
            case Phase.PlayerTurn: UpdatePlayer(); break;
            case Phase.EnemyTurn: UpdateEnemy(); break;
            case Phase.Win:
            case Phase.Lose: HandleOverlayClick(); break;
        }

        CheckEnd();
    }

    void CheckEnd()
    {
        if (Phase != Phase.PlayerTurn && Phase != Phase.EnemyTurn) return;
        if (_anims.Count > 0) return;
        if (AliveEnemies().Count == 0) { Phase = Phase.Win; }
        else if (AlivePlayers().Count == 0) { Phase = Phase.Lose; }
    }

    // ---------------- player turn ----------------
    void UpdatePlayer()
    {
        // keep selection valid
        if (Selected != null && !Selected.Alive) Selected = null;
        if (Selected == null || !Selected.CanAct)
        {
            var next = Players.FirstOrDefault(p => p.CanAct);
            if (next != null && (Selected == null || !Selected.CanAct)) Selected = next ?? Selected;
        }

        RecomputeMoveCost();
        UpdateHoverAndAim();
        HandlePlayerInput();
    }

    void RecomputeMoveCost()
    {
        if (Selected != null && Selected.Team == Team.Player && Selected.CanAct)
        {
            Func<int, int, bool> blocked = (x, y) => IsOccupiedByOther(x, y, Selected);
            MoveCost = Grid.CostMap(Selected.X, Selected.Y, blocked, out _cameFrom, Selected.MoveBudget * 2);
        }
        else { MoveCost = null; _cameFrom = null; }
    }

    void UpdateHoverAndAim()
    {
        ShowOdds = false;
        PathPreview.Clear();
        var m = Raylib.GetMousePosition();
        HoverValid = Util.ScreenToTile(m, out HoverX, out HoverY);

        Unit hovered = HoverValid ? UnitAt(HoverX, HoverY) : null;

        if (AimMode)
        {
            if (hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered))
                AimTarget = hovered;
            if (AimTarget != null && !AimTarget.Alive) AimTarget = FirstTargetFor(Selected);
            AimValid = AimTarget != null && CanTarget(Selected, AimTarget);
            if (AimTarget != null)
            {
                ShowOdds = true;
                HoverOdds = Combat.ComputeOdds(Grid, Selected, AimTarget);
            }
            return;
        }

        // hovering an enemy we could shoot -> show odds
        if (Selected != null && Selected.CanAct && Selected.Ammo > 0 &&
            hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered))
        {
            ShowOdds = true;
            HoverOdds = Combat.ComputeOdds(Grid, Selected, hovered);
        }
        // path preview to reachable floor
        else if (Selected != null && Selected.CanAct && MoveCost != null && HoverValid &&
                 Grid.IsFloor(HoverX, HoverY) && MoveCost[HoverX, HoverY] > 0)
        {
            int c = MoveCost[HoverX, HoverY];
            bool dash = c > Selected.MoveBudget;
            if (!dash || Selected.ActionsLeft >= 2)
                PathPreview = Grid.ReconstructPath(_cameFrom, Selected.X, Selected.Y, HoverX, HoverY);
        }
    }

    void HandlePlayerInput()
    {
        // keys
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { EndPlayerTurn(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) CycleSelection();
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { AimMode = false; }
        if (Raylib.IsKeyPressed(KeyboardKey.One)) ToggleAim();
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) DoOverwatch();
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) DoHunker();
        if (Raylib.IsKeyPressed(KeyboardKey.R)) DoReload();

        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { AimMode = false; return; }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var m = Raylib.GetMousePosition();
            // HUD first
            if (Raylib.CheckCollisionPointRec(m, Hud.EndTurnRect)) { EndPlayerTurn(); return; }
            foreach (var b in Hud.ActionButtons)
                if (b.Enabled && Raylib.CheckCollisionPointRec(m, b.Rect)) { DoAction(b.Id); return; }

            // board click
            if (!HoverValid) return;
            var hovered = UnitAt(HoverX, HoverY);

            if (AimMode)
            {
                if (hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered))
                    IssueShoot(hovered);
                else
                    AimMode = false;
                return;
            }

            if (hovered != null && hovered.Team == Team.Player) { SelectUnit(hovered); return; }
            if (hovered != null && hovered.Team == Team.Enemy && Selected != null &&
                Selected.CanAct && CanTarget(Selected, hovered)) { IssueShoot(hovered); return; }
            if (Selected != null && Selected.CanAct && MoveCost != null &&
                Grid.IsFloor(HoverX, HoverY) && MoveCost[HoverX, HoverY] > 0)
                IssueMove(HoverX, HoverY);
        }
    }

    void DoAction(string id)
    {
        switch (id)
        {
            case "shoot": ToggleAim(); break;
            case "overwatch": DoOverwatch(); break;
            case "hunker": DoHunker(); break;
            case "reload": DoReload(); break;
        }
    }

    void SelectUnit(Unit u) { Selected = u; AimMode = false; }

    void CycleSelection()
    {
        var actable = Players.Where(p => p.CanAct).ToList();
        if (actable.Count == 0) return;
        int idx = Selected != null ? actable.IndexOf(Selected) : -1;
        Selected = actable[(idx + 1) % actable.Count];
        AimMode = false;
    }

    void ToggleAim()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (AimMode) { AimMode = false; return; }
        if (!HasAnyTarget(Selected)) return;
        AimMode = true;
        AimTarget = FirstTargetFor(Selected);
    }

    void IssueMove(int tx, int ty)
    {
        if (MoveCost == null) return;
        int c = MoveCost[tx, ty];
        if (c <= 0) return;
        int need = c <= Selected.MoveBudget ? 1 : 2;
        if (need > Selected.ActionsLeft) return;
        var path = Grid.ReconstructPath(_cameFrom, Selected.X, Selected.Y, tx, ty);
        if (path.Count == 0) return;
        Selected.ActionsLeft -= need;
        foreach (var (px, py) in path) Enqueue(new MoveStepAnim(Selected, px, py), Team.Player);
        AimMode = false;
        PathPreview.Clear();
    }

    void IssueShoot(Unit target)
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (!CanTarget(Selected, target)) return;
        Selected.Ammo--;
        Selected.ActionsLeft = 0;
        var res = Combat.Resolve(Grid, Selected, target);
        Enqueue(new ShotAnim(Selected, target, res), Team.Player);
        AimMode = false;
    }

    void DoOverwatch()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        Selected.OnOverwatch = true;
        Selected.ActionsLeft = 0;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
        AimMode = false;
    }

    void DoHunker()
    {
        if (Selected == null || !Selected.CanAct) return;
        Selected.Hunkered = true;
        Selected.ActionsLeft = 0;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "HUNKERED", Pal.Good, 18f);
        AimMode = false;
    }

    void DoReload()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo >= Selected.Weapon.Clip) return;
        Selected.Ammo = Selected.Weapon.Clip;
        Selected.ActionsLeft -= 1;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "RELOAD", Pal.TxtDim, 18f);
        AimMode = false;
    }

    void EndPlayerTurn()
    {
        AimMode = false;
        Selected = null;
        MoveCost = null;
        Phase = Phase.EnemyTurn;
        foreach (var e in Enemies) if (e.Alive) e.BeginTurn();
        _aiUnits = AliveEnemies();
        _aiIdx = 0;
        _aiStage = AiStage.PickNext;
        _aiPlan = null;
        ShowBanner("ENEMY TURN", true);
        Enqueue(new WaitAnim(0.5f), Team.Enemy);
    }

    void StartPlayerTurn()
    {
        _turnCount++;
        Phase = Phase.PlayerTurn;
        foreach (var p in Players) if (p.Alive) p.BeginTurn();
        foreach (var e in Enemies) if (e.Alive) e.ReactedThisTurn = false; // enemy OW can react next turn
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        ShowBanner("PLAYER TURN", false);
    }

    // ---------------- enemy turn ----------------
    void UpdateEnemy()
    {
        if (BannerTimer > 0.55f) return; // let banner breathe before acting

        if (_aiStage == AiStage.PickNext)
        {
            while (_aiIdx < _aiUnits.Count && !_aiUnits[_aiIdx].Alive) _aiIdx++;
            if (_aiIdx >= _aiUnits.Count) { StartPlayerTurn(); return; }

            var e = _aiUnits[_aiIdx];
            _aiPlan = Ai.Plan(this, e);

            if (_aiPlan.Path.Count > 0)
            {
                e.ActionsLeft -= _aiPlan.MoveActions;
                foreach (var (px, py) in _aiPlan.Path) Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);
            }
            _aiStage = AiStage.ActAfterMove;
            return;
        }

        if (_aiStage == AiStage.ActAfterMove)
        {
            var e = _aiUnits[_aiIdx];
            if (e.Alive)
            {
                if (_aiPlan.ShootTarget != null && _aiPlan.ShootTarget.Alive &&
                    e.ActionsLeft > 0 && e.Ammo > 0 && CanTarget(e, _aiPlan.ShootTarget))
                {
                    e.Ammo--;
                    e.ActionsLeft = 0;
                    var res = Combat.Resolve(Grid, e, _aiPlan.ShootTarget);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new ShotAnim(e, _aiPlan.ShootTarget, res), Team.Enemy);
                }
                else if (_aiPlan.Overwatch && e.ActionsLeft > 0 && e.Ammo > 0)
                {
                    e.OnOverwatch = true; e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 16f);
                }
                else if (_aiPlan.Hunker && e.ActionsLeft > 0)
                {
                    e.Hunkered = true; e.ActionsLeft = 0;
                }
            }
            _aiIdx++;
            _aiStage = AiStage.PickNext;
        }
    }

    // ---------------- overlay click ----------------
    void HandleOverlayClick()
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var m = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(m, Hud.OverlayBtn))
            {
                if (Phase == Phase.Intro) StartMission();
                else StartMission(); // redeploy
            }
        }
        if (Phase == Phase.Intro && Raylib.IsKeyPressed(KeyboardKey.Enter)) StartMission();
    }

    // ---------------- draw ----------------
    public void Draw()
    {
        Raylib.ClearBackground(Pal.Bg);

        var cam = new Camera2D
        {
            Target = Vector2.Zero,
            Offset = Fx.ShakeOffset,
            Rotation = 0f,
            Zoom = 1f,
        };
        Raylib.BeginMode2D(cam);
        Renderer.DrawBoard(this);
        Raylib.EndMode2D();

        Hud.Draw(this);
    }
}
