using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Breach;

public enum Phase { Intro, PlayerTurn, EnemyTurn, Barracks, Win, Lose }
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

    // campaign run
    Run _run = new();
    public Run RunState => _run;

    // game-feel: hit-stop freeze + camera zoom-punch
    public float HitStop;
    float _camPulse;
    public void AddHitStop(float s) { HitStop = MathF.Max(HitStop, s); }
    public void AddZoomPunch(float p) { _camPulse = MathF.Max(_camPulse, p); }

    // ---------------- lifecycle ----------------
    /// Start a brand-new campaign run (called from intro / after a run ends).
    public void StartMission()
    {
        _run = new Run();
        _run.Start();
        Players = _run.Squad;
        SetupMission(1);
    }

    void SetupMission(int n)
    {
        _run.Mission = n;
        Players = _run.Squad;              // living roster only
        Mission.Build(Grid, Players, Enemies, n);
        Fx.Particles.Clear();
        Fx.Texts.Clear();
        _anims.Clear();
        HitStop = 0;
        _turnCount = 1;
        Phase = Phase.PlayerTurn;
        foreach (var u in Players) u.BeginTurn();
        foreach (var u in Enemies) { u.BeginTurn(); u.OnOverwatch = false; }
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        ShowBanner($"MISSION {n}", false);
    }

    void NextMission() => SetupMission(_run.Mission + 1);

    void EnterBarracks()
    {
        _run.Squad = AlivePlayers();
        _run.DebriefSurvivors(_run.Squad);
        if (_run.Mission >= Run.MaxMissions) { Phase = Phase.Win; Audio.Play("win"); }
        else { Phase = Phase.Barracks; Audio.Play("win"); }
    }

    void ShowBanner(string text, bool enemy)
    {
        BannerText = text; BannerEnemy = enemy;
        BannerMax = BannerTimer = 1.2f;
        Audio.Play("turn");
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
            Audio.Play("over");
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
        if (d.Team == Team.Player) _run.Fallen.Add(d.Name);
        Color c = d.Team == Team.Player ? Pal.Friend : Pal.Foe;
        Fx.Burst(d.Pos, c, 30, 280f, 0.7f, 4f, true);
        Fx.Burst(d.Pos, Pal.RGBA(20, 25, 33), 16, 150f, 0.8f, 5f);
        Fx.PopText(d.Pos + new Vector2(0, -10), "DOWN", c, 22f);
        Fx.AddShake(7f);
        AddHitStop(0.1f);
        AddZoomPunch(0.05f);
        Audio.Play("death");
        // purge any queued movement for the dead unit
        _anims.RemoveAll(a => a is MoveStepAnim m && m.Unit == d);
        if (Selected == d) Selected = null;
    }

    // ---------------- update ----------------
    public void Update(float dt)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.M)) Audio.ToggleMute();

        // camera zoom-punch always relaxes; hit-stop freezes the rest of the sim
        _camPulse *= MathF.Exp(-dt * 11f);
        if (_camPulse < 0.001f) _camPulse = 0;
        if (HitStop > 0) { HitStop -= dt; return; }

        float t = MathF.Min(dt, 0.05f);
        Fx.Update(t);
        foreach (var u in Players) { u.Flash = MathF.Max(0, u.Flash - t * 4f); u.Recoil *= MathF.Exp(-t * 17f); }
        foreach (var u in Enemies) { u.Flash = MathF.Max(0, u.Flash - t * 4f); u.Recoil *= MathF.Exp(-t * 17f); }
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
            case Phase.Barracks:
                if (AutoPlay) NextMission(); else HandleOverlayClick();
                break;
            case Phase.Win:
            case Phase.Lose: HandleOverlayClick(); break;
        }

        CheckEnd();
    }

    void CheckEnd()
    {
        if (Phase != Phase.PlayerTurn && Phase != Phase.EnemyTurn) return;
        if (_anims.Count > 0) return;
        if (AliveEnemies().Count == 0) EnterBarracks();
        else if (AlivePlayers().Count == 0) { Phase = Phase.Lose; Audio.Play("lose"); }
    }

    // ---------------- player turn ----------------
    // Test-only autopilot (enabled via BREACH_AUTOPLAY): drives real player actions
    // so the whole loop can be exercised headlessly. Never enabled in normal play.
    public bool AutoPlay;
    void AutoStep()
    {
        if (_anims.Count > 0 || Phase != Phase.PlayerTurn) return;
        var u = Players.FirstOrDefault(p => p.CanAct);
        if (u == null) { EndPlayerTurn(); return; }
        Selected = u;
        RecomputeMoveCost();

        var tgt = FirstTargetFor(u);
        if (tgt != null && u.Ammo > 0) { IssueShoot(tgt); return; }
        if (u.Ammo == 0) { DoReload(); return; }

        var enemy = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        if (enemy != null && MoveCost != null)
        {
            int bx = -1, by = -1; float best = Util.TileDist(u.X, u.Y, enemy.X, enemy.Y);
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    int c = MoveCost[x, y];
                    if (c <= 0 || c > u.MoveBudget) continue;
                    float d = Util.TileDist(x, y, enemy.X, enemy.Y);
                    if (d < best) { best = d; bx = x; by = y; }
                }
            if (bx >= 0) { IssueMove(bx, by); return; }
        }
        DoHunker(); // guarantees progress
    }

    void UpdatePlayer()
    {
        if (AutoPlay) { AutoStep(); return; }
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

    void SelectUnit(Unit u) { Selected = u; AimMode = false; Audio.Play("select"); }

    void CycleSelection()
    {
        var actable = Players.Where(p => p.CanAct).ToList();
        if (actable.Count == 0) return;
        int idx = Selected != null ? actable.IndexOf(Selected) : -1;
        Selected = actable[(idx + 1) % actable.Count];
        AimMode = false;
        Audio.Play("select");
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
        Audio.Play("move");
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
        Audio.Play("over");
        AimMode = false;
    }

    void DoHunker()
    {
        if (Selected == null || !Selected.CanAct) return;
        Selected.Hunkered = true;
        Selected.ActionsLeft = 0;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "HUNKERED", Pal.Good, 18f);
        Audio.Play("hunker");
        AimMode = false;
    }

    void DoReload()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo >= Selected.Weapon.Clip) return;
        Selected.Ammo = Selected.Weapon.Clip;
        Selected.ActionsLeft -= 1;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "RELOAD", Pal.TxtDim, 18f);
        Audio.Play("reload");
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
                Audio.Play("move");
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
                    Audio.Play("over");
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
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn);
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter);
        if (!click && !enter) return;

        if (Phase == Phase.Barracks) NextMission();   // deploy to next mission
        else StartMission();                           // intro / win / lose -> new run
    }

    // ---------------- draw ----------------
    public void Draw()
    {
        Raylib.ClearBackground(Pal.Bg);

        var bc = new Vector2(Cfg.OriginX + Cfg.BoardW / 2f, Cfg.OriginY + Cfg.BoardH / 2f);
        var cam = new Camera2D
        {
            Target = bc,
            Offset = bc + Fx.ShakeOffset,
            Rotation = 0f,
            Zoom = 1f + _camPulse,
        };
        Raylib.BeginMode2D(cam);
        Renderer.DrawBoard(this);
        Raylib.EndMode2D();

        Hud.Draw(this);
    }
}
