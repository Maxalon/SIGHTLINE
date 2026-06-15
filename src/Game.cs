using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

public enum Phase { Intro, PlayerTurn, EnemyTurn, Barracks, Win, Lose }
public enum Objective { Eliminate, Evac, Hack, Escort }
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
    public bool[,] Threat;          // reachable tiles exposed to active-enemy fire (no cover)
    (int, int)[,] _cameFrom;
    public List<(int x, int y)> PathPreview = new();

    // aiming
    public bool AimMode;
    public Unit AimTarget;
    public bool AimValid;
    public bool ShowOdds;
    public ShotOdds HoverOdds;

    // grenade targeting
    public const int GrenadeRange = 7;
    public bool GrenadeMode;
    public int GrenTx, GrenTy;
    public bool GrenValid;

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

    // run persistence: when false (normal play) the run is checkpointed to disk at
    // each mission start and CONTINUE is offered on the intro. The headless harness
    // sets this true so the smoke test never touches the save file.
    public bool NoPersist;

    // barracks requisition shop: spend Intel before choosing the next deployment.
    // _shopDone gates the barracks flow (shop -> promotions -> deployment cards).
    bool _shopDone = true;
    public bool ShopDone => _shopDone;
    public static readonly int[] ShopCost = { 6, 10, 16, 12 };
    public static readonly string[] ShopName = { "FIELD MEDKIT", "COMBAT STIMS", "ADV. TRAINING", "FRAG CACHE" };
    public static readonly string[] ShopDesc =
    {
        "Heal your most-wounded soldier to full.",
        "+2 max HP to your frailest soldier (permanent).",
        "Grant a soldier a bonus perk choice.",
        "+1 grenade every mission for a soldier (permanent).",
    };

    // mission objective
    public Objective Objective;
    public List<(int x, int y)> EvacZone = new();

    // per-mission visual theme
    public Biome Biome = Biome.All[0];

    // escort objective: a fragile VIP that must reach the extraction zone alive.
    // Mission-only — it rides in Players for the mission but never joins the squad.
    public Unit Vip;

    // hack objective: reach the terminal and hack it over several actions
    public const int HackRequired = 3;
    public (int x, int y) Terminal;
    public int HackProgress;
    public bool HasTerminal => Objective == Objective.Hack;
    public bool CanHack(Unit u) =>
        HasTerminal && u != null && u.Team == Team.Player && u.CanAct &&
        HackProgress < HackRequired && Util.ChebyDist(u.X, u.Y, Terminal.x, Terminal.y) <= 1;

    // end-turn confirmation when soldiers still have actions
    public bool EndTurnArmed;
    int _lastSig = -1;

    // camera: player-controlled zoom/pan for readability (identity by default,
    // so default mouse picking + the headless harness are unaffected)
    public float CamZoom = 1f;
    public Vector2 CamPan = Vector2.Zero;

    // keyboard tile cursor (mouse-free play): arrows/WASD move it, Space acts
    public bool KbCursor;
    public int CurX, CurY;

    // pause / settings overlay
    public bool Paused;
    public bool ShowThreatPref = true;

    // custom-tag text editor (modal): key T in a mission, or from the perk chooser
    public bool EditingTag;
    public string TagBuffer = "";
    public Unit TagTarget;

    // game-feel: hit-stop freeze + camera zoom-punch
    public float HitStop;
    float _camPulse;
    public void AddHitStop(float s) { HitStop = MathF.Max(HitStop, s); }
    public void AddZoomPunch(float p) { _camPulse = MathF.Max(_camPulse, p); }

    // ---------------- lifecycle ----------------
    /// Start a brand-new campaign run (called from intro / after a run ends).
    /// startAt lets the headless harness jump straight to a given mission.
    public void StartMission(int startAt = 1)
    {
        _run = new Run();
        _run.Start();
        Players = _run.Squad;
        int n = Util.Clamp(startAt, 1, Run.MaxMissions);
        _run.CurrentCard = Run.StandardCard(n);
        SetupMission(n);
    }

    void SetupMission(int n)
    {
        _run.Mission = n;
        // per-mission roster: a copy of the persistent squad (+ an optional VIP),
        // so adding the escort asset never pollutes the campaign squad.
        Players = new List<Unit>(_run.Squad);

        // objective + difficulty come from the chosen deployment card (Run.ObjectiveFor baseline)
        var card = _run.CurrentCard ?? Run.StandardCard(n);
        Objective = card.Objective;
        EvacZone.Clear();
        HackProgress = 0;
        Vip = null;
        // both Evac and Escort extract to the same top-right zone
        if (Objective == Objective.Evac || Objective == Objective.Escort)
        {
            EvacZone.Add((Grid.W - 2, 0)); EvacZone.Add((Grid.W - 1, 0));
            EvacZone.Add((Grid.W - 2, 1)); EvacZone.Add((Grid.W - 1, 1));
        }
        if (Objective == Objective.Hack)
            Terminal = (Grid.W / 2 + 1, Grid.H / 2);
        if (Objective == Objective.Escort)
        {
            Vip = Mission.MakeVip();
            Vip.X = 2; Vip.Y = 5;          // valid pre-build tile (spawn table refines it)
            Players.Add(Vip);
        }

        Mission.Build(Grid, Players, Enemies, n, EvacZone, HasTerminal ? Terminal : null, card.EnemyDelta, card.StatDelta);
        if (Vip != null) { Vip.Grenades = 0; Vip.AbilityCharge = 0; }  // the asset has no kit
        Fx.Particles.Clear();
        Fx.Texts.Clear();
        _anims.Clear();
        HitStop = 0;
        _turnCount = 1;
        _autoSig = -1; _autoStall = 0;
        Phase = Phase.PlayerTurn;
        foreach (var u in Players) u.BeginTurn();
        foreach (var u in Enemies) { u.BeginTurn(); u.OnOverwatch = false; }
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        Biome = Biome.For(n);
        ShowBanner($"MISSION {n} - {Biome.Name}", false);

        // checkpoint the run at the start of each mission (normal play only)
        if (!NoPersist) SaveGame.Save(_run);
    }

    void NextMission() => SetupMission(_run.Mission + 1);

    /// Resume a saved campaign from the intro. Reloads the run and restarts its
    /// current mission from the start; returns false if there is no readable save.
    public bool ContinueRun()
    {
        var run = SaveGame.Load();
        if (run == null || run.Squad == null || run.Squad.Count == 0) return false;
        _run = run;
        Players = _run.Squad;
        int n = Util.Clamp(_run.Mission < 1 ? 1 : _run.Mission, 1, Run.MaxMissions);
        _run.CurrentCard ??= Run.StandardCard(n);
        SetupMission(n);
        return true;
    }

    /// Harness hook (screenshot only): reveal all dormant enemies.
    public void DebugWakeAll() { foreach (var e in Enemies) if (e.Alive) e.Active = true; }

    /// Harness hook (screenshot only): force a barracks rank-up perk choice.
    public void DebugBarracksPerk()
    {
        if (_run.Squad.Count > 0)
        {
            _run.Squad[0].Kills = 3;
            _run.Squad[0].Perks.Add(Perk.Reflexes);   // show an existing perk in the dossier
            _run.Squad[0].Hp = Math.Max(1, _run.Squad[0].Hp - 3);
        }
        _run.DebriefSurvivors();
        Phase = Phase.Barracks;
    }

    /// Harness hook (screenshot only): show the barracks deployment-card screen.
    public void DebugDeployCards()
    {
        _run.DebriefSurvivors();
        _run.GenerateOffers(_run.Mission + 1);
        Phase = Phase.Barracks;
    }

    void EnterBarracks()
    {
        _run.Squad = AlivePlayers().Where(u => !u.IsVip).ToList();  // the VIP never joins the squad
        int survivors = _run.Squad.Count;
        bool finished = _run.Mission >= Run.MaxMissions;

        // reward for clearing the chosen deployment
        if (!finished && _run.CurrentCard != null && _run.CurrentCard.Reward == RewardKind.Heal)
            foreach (var u in _run.Squad) u.Hp = u.MaxHp;

        _run.DebriefSurvivors();

        if (!finished && _run.CurrentCard != null && _run.CurrentCard.Reward == RewardKind.BonusPerk)
            _run.AddBonusPerk();

        if (finished) { Phase = Phase.Win; Audio.Play("win"); if (!NoPersist) SaveGame.Delete(); }
        else
        {
            // intel salvage scales with survivors + depth; ONSLAUGHT pays a risk premium
            int gained = 8 + 3 * survivors + _run.Mission;
            if (_run.CurrentCard != null && _run.CurrentCard.ModName == "ONSLAUGHT") gained += 6;
            _run.Intel += gained;
            _run.Report.Insert(0, $"Recovered {gained} intel  (total {_run.Intel})");
            _shopDone = false;
            _run.GenerateOffers(_run.Mission + 1);
            Phase = Phase.Barracks;
            Audio.Play("win");
        }
    }

    // run-over screen text (set by LoseRun so the cause reads accurately)
    public string LoseTitle = "RUN OVER";
    public string LoseReason = "";

    /// End the run as a loss and clear the checkpoint so the intro stops offering CONTINUE.
    void LoseRun(string title, string reason)
    {
        LoseTitle = title;
        LoseReason = reason;
        Phase = Phase.Lose;
        Audio.Play("lose");
        if (!NoPersist) SaveGame.Delete();
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

    // Note: OnStart runs when the anim becomes active (see the advance loop), NOT at
    // enqueue time — otherwise every queued step of a multi-tile path captures its
    // _from at the original tile and the unit snaps back to the start each step.
    void Enqueue(Anim a, Team owner) { AnimOwner = owner; _anims.Add(a); }

    // ---------------- combat events ----------------
    public void OnUnitEnteredTile(Unit mover)
    {
        if (!mover.Alive) return;
        if (mover.Team == Team.Player) CheckPodActivation();  // reveal pods while advancing
        var watchers = mover.Team == Team.Player ? Enemies : Players;
        int insertAt = 1;
        foreach (var w in watchers)
        {
            if (!w.Alive || !w.OnOverwatch || w.ReactedThisTurn || w.Ammo <= 0) continue;
            if (!CanTarget(w, mover)) continue;
            w.OnOverwatch = false;
            w.ReactedThisTurn = true;
            w.Ammo--;
            int reactMod = w.HasPerk(Perk.Reflexes) ? 100 : -10; // Reflexes: overwatch rarely misses
            var res = Combat.Resolve(Grid, w, mover, reactMod);
            Fx.PopText(w.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
            Audio.Play("over");
            var shot = new ShotAnim(w, mover, res, reaction: true);
            // OnStart runs when this reaction becomes the active anim (Started is false),
            // by which point the mover has settled on the reacted-to tile.
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
        Fx.PopText(d.Pos + new Vector2(0, -10), d.IsVip ? "VIP DOWN" : "DOWN", c, 22f);
        if (d.IsVip) { ShowBanner("VIP DOWN", true); Fx.AddShake(13f); }
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
        // custom-tag editor is modal: it swallows all other input while open
        if (EditingTag) { UpdateTagEditor(); return; }

        if (Raylib.IsKeyPressed(KeyboardKey.M)) Audio.ToggleMute();
        if (!AutoPlay && Raylib.IsKeyPressed(KeyboardKey.F)) Display.ToggleFullscreen();

        // camera zoom-punch always relaxes; hit-stop freezes the rest of the sim
        _camPulse *= MathF.Exp(-dt * 11f);
        if (_camPulse < 0.001f) _camPulse = 0;
        if (HitStop > 0) { HitStop -= dt; return; }

        // pause/settings overlay + camera controls (live play only, never in autoplay)
        if (!AutoPlay && (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (AimMode || GrenadeMode) { AimMode = false; GrenadeMode = false; }
                else Paused = !Paused;
            }
            if (Paused) { HandlePauseMenu(); return; }
            HandleCamera();
        }

        float t = MathF.Min(dt, 0.05f);
        Fx.Update(t);
        foreach (var u in Players) { u.Flash = MathF.Max(0, u.Flash - t * 4f); u.Recoil *= MathF.Exp(-t * 17f); }
        foreach (var u in Enemies) { u.Flash = MathF.Max(0, u.Flash - t * 4f); u.Recoil *= MathF.Exp(-t * 17f); }
        if (BannerTimer > 0) BannerTimer -= t;

        // advance animation queue
        if (_anims.Count > 0)
        {
            var a = _anims[0];
            if (!a.Started) { a.Started = true; a.OnStart(this); }
            if (a.Update(this, t)) _anims.RemoveAt(0);
            return;
        }

        switch (Phase)
        {
            case Phase.Intro: HandleOverlayClick(); break;
            case Phase.PlayerTurn: UpdatePlayer(); break;
            case Phase.EnemyTurn: UpdateEnemy(); break;
            case Phase.Barracks:
                if (!_shopDone)                          // spend intel first (requisition)
                {
                    if (AutoPlay) AutoShop(); else HandleShopClick();
                }
                else if (_run.PendingPerks.Count > 0)    // then resolve rank-up perk picks
                {
                    if (AutoPlay) ChoosePerk(0); else HandlePerkClick();
                }
                else if (AutoPlay) ChooseCard(0);        // then choose the next deployment
                else HandleCardClick();
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
        var alivePlayers = AlivePlayers();
        if (alivePlayers.Count == 0) { LoseRun("RUN OVER", $"The squad fell on mission {_run.Mission}."); return; }

        if (Objective == Objective.Eliminate)
        {
            if (AliveEnemies().Count == 0) EnterBarracks();
        }
        else if (Objective == Objective.Hack) // hold the terminal until it's fully hacked
        {
            if (HackProgress >= HackRequired) EnterBarracks();
        }
        else if (Objective == Objective.Escort) // get the VIP to extraction; losing it is a wipe
        {
            if (Vip == null || !Vip.Alive) { LoseRun("VIP LOST", $"The asset was lost on mission {_run.Mission}."); return; }
            if (EvacZone.Contains((Vip.X, Vip.Y))) EnterBarracks();
        }
        else // Evac: every surviving soldier must stand in the extraction zone
        {
            if (alivePlayers.All(p => EvacZone.Contains((p.X, p.Y)))) EnterBarracks();
        }
    }

    // ---------------- player turn ----------------
    // Test-only autopilot (enabled via SIGHTLINE_AUTOPLAY): drives real player actions
    // so the whole loop can be exercised headlessly. Never enabled in normal play.
    public bool AutoPlay;

    // Stall guard for the headless autopilot: if no progress is made for several
    // player turns (e.g. only unreachable dormant pods remain), force a pod awake
    // so the match always resolves. Test-only; never runs in normal play.
    int _autoSig = -1, _autoStall;
    void AutoStallCheck()
    {
        int sig = AliveEnemies().Count * 1000
                + AliveEnemies().Count(e => e.Active) * 10
                + HackProgress
                + AlivePlayers().Count(p => EvacZone.Contains((p.X, p.Y)));
        if (sig != _autoSig) { _autoSig = sig; _autoStall = 0; return; }
        if (++_autoStall < 10) return;
        _autoStall = 0;
        var dormant = Enemies.Where(e => e.Alive && !e.Active).ToList();
        if (dormant.Count > 0) ActivatePod(dormant[0].PodId);
    }

    void AutoStep()
    {
        if (_anims.Count > 0 || Phase != Phase.PlayerTurn) return;
        var u = Players.FirstOrDefault(p => p.CanAct);
        if (u == null) { EndPlayerTurn(); return; }
        Selected = u;
        RecomputeMoveCost();

        // EVAC objective: get everyone to the extraction zone
        if (Objective == Objective.Evac)
        {
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

        // ESCORT objective: walk the VIP to extraction; soldiers screen for it
        if (Objective == Objective.Escort)
        {
            if (u.IsVip)
            {
                if (!EvacZone.Contains((u.X, u.Y)))
                {
                    var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                       .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
                    if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return;
                }
                DoHunker(); return;        // arrived or no path this turn
            }
            var st = FirstTargetFor(u);
            if (st != null && u.Ammo > 0) { IssueShoot(st); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (foe != null && TryMoveTowardTile(u, foe.X, foe.Y)) return;
            DoHunker(); return;
        }

        // ELIMINATE objective
        // (test) exercise the class signature ability to keep its path covered
        if (CanAbility(u) && Util.Roll(45))
        {
            var kind = u.Ability;
            DoAbility();
            if (kind == AbilityKind.Steady || kind == AbilityKind.Suppress) return; // spent an action
            // RunGun / Blitz are free stances — fall through and act with them
        }

        var tgt = FirstTargetFor(u);
        if (tgt != null && u.Ammo > 0) { IssueShoot(tgt); return; }
        if (u.Ammo == 0) { DoReload(); return; }

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

    // ---------------- activation pods ----------------
    public const int SightRange = 12;

    void CheckPodActivation()
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Active) continue;
            foreach (var p in Players)
            {
                if (!p.Alive) continue;
                if (Util.TileDist(p.X, p.Y, e.X, e.Y) > SightRange) continue;
                if (!Grid.HasLineOfSight(p.X, p.Y, e.X, e.Y)) continue;
                ActivatePod(e.PodId);
                break;
            }
        }
    }

    public void ActivatePod(int podId)
    {
        bool any = false;
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Active || e.PodId != podId) continue;
            e.Active = true;
            any = true;
            // free scatter toward cover/line of fire (move only, no shot)
            var plan = Ai.Plan(this, e);
            foreach (var (px, py) in plan.Path) Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);
        }
        if (any)
        {
            BannerText = "CONTACT!"; BannerEnemy = true; BannerMax = BannerTimer = 1.0f;
            Fx.AddShake(3f);
            Audio.Play("over");
        }
    }

    void UpdatePlayer()
    {
        if (AutoPlay) { AutoStep(); return; }

        CheckPodActivation();
        if (_anims.Count > 0) return;   // a pod just activated — let the scatter play

        // clear the end-turn confirmation if anything changed (action taken / reselect)
        int sig = AlivePlayers().Sum(p => p.ActionsLeft) * 8 + (Selected != null ? Players.IndexOf(Selected) : 0);
        if (sig != _lastSig) { EndTurnArmed = false; _lastSig = sig; }

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
            ComputeThreat();
        }
        else { MoveCost = null; _cameFrom = null; Threat = null; }
    }

    // Mark each reachable tile (and the current one) that a live, active enemy could
    // fire on with no cover for the mover — i.e. tiles you'd be exposed standing on.
    void ComputeThreat()
    {
        if (!ShowThreatPref) { Threat = null; return; }   // disabled in settings
        Threat = new bool[Grid.W, Grid.H];
        var foes = Enemies.Where(e => e.Alive && e.Active && e.Ammo > 0).ToList();
        if (foes.Count == 0) return;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                bool here = x == Selected.X && y == Selected.Y;
                if (!here && MoveCost[x, y] <= 0) continue;
                foreach (var e in foes)
                {
                    if (Util.TileDist(x, y, e.X, e.Y) > e.Weapon.MaxRange) continue;
                    if (!Grid.HasLineOfSight(e.X, e.Y, x, y)) continue;
                    if (Grid.GetCover(x, y, e.X, e.Y).Level == 0) { Threat[x, y] = true; break; }
                }
            }
    }

    void UpdateHoverAndAim()
    {
        ShowOdds = false;
        PathPreview.Clear();
        // mouse -> board tile, through the (stable) picking camera so zoom/pan work
        var world = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), ViewCamera(false));
        HoverValid = Util.ScreenToTile(world, out HoverX, out HoverY);
        if (KbCursor) { HoverX = CurX; HoverY = CurY; HoverValid = Grid.InBounds(CurX, CurY); }

        Unit hovered = HoverValid ? UnitAt(HoverX, HoverY) : null;

        if (GrenadeMode)
        {
            GrenTx = HoverX; GrenTy = HoverY;
            GrenValid = HoverValid && Selected != null &&
                        Util.TileDist(Selected.X, Selected.Y, HoverX, HoverY) <= GrenadeRange;
            return;
        }

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
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { RequestEndTurn(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) CycleSelection();
        if (Raylib.IsKeyPressed(KeyboardKey.One)) ToggleAim();
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) DoOverwatch();
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) DoHunker();
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) ToggleGrenade();
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) DoAbility();
        if (Raylib.IsKeyPressed(KeyboardKey.H)) DoHack();
        if (Raylib.IsKeyPressed(KeyboardKey.R)) DoReload();
        if (Raylib.IsKeyPressed(KeyboardKey.T)) { OpenTagEditor(Selected); return; }

        // keyboard tile cursor: arrows / WASD move it, Space acts on it
        int cdx = 0, cdy = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) cdy = -1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) cdy = 1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A)) cdx = -1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D)) cdx = 1;
        if (cdx != 0 || cdy != 0) MoveCursor(cdx, cdy);
        if (Raylib.IsKeyPressed(KeyboardKey.Space) && HoverValid) { BoardAct(HoverX, HoverY); return; }
        if (KbCursor && Raylib.GetMouseDelta() != Vector2.Zero) KbCursor = false;  // mouse takes back over

        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { AimMode = false; GrenadeMode = false; return; }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            KbCursor = false;
            var m = Raylib.GetMousePosition();
            // HUD first (screen space — not affected by the board camera)
            if (Raylib.CheckCollisionPointRec(m, Hud.EndTurnRect)) { RequestEndTurn(); return; }
            foreach (var b in Hud.ActionButtons)
                if (b.Enabled && Raylib.CheckCollisionPointRec(m, b.Rect)) { DoAction(b.Id); return; }
            foreach (var c in Hud.RosterChips)
                if (Raylib.CheckCollisionPointRec(m, c.rect)) { SelectUnit(c.unit); return; }

            if (!HoverValid) return;
            BoardAct(HoverX, HoverY);
        }
    }

    // Resolve a board action on tile (hx,hy): throw / aim / select / fire / move.
    // Shared by mouse clicks and the keyboard cursor.
    void BoardAct(int hx, int hy)
    {
        if (!Grid.InBounds(hx, hy)) return;
        var hovered = UnitAt(hx, hy);

        if (GrenadeMode)
        {
            if (GrenValid) IssueGrenade(hx, hy);
            else GrenadeMode = false;
            return;
        }
        if (AimMode)
        {
            if (hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered)) IssueShoot(hovered);
            else AimMode = false;
            return;
        }
        if (hovered != null && hovered.Team == Team.Player) { SelectUnit(hovered); return; }
        if (hovered != null && hovered.Team == Team.Enemy && Selected != null &&
            Selected.CanAct && CanTarget(Selected, hovered)) { IssueShoot(hovered); return; }
        if (Selected != null && Selected.CanAct && MoveCost != null &&
            Grid.IsFloor(hx, hy) && MoveCost[hx, hy] > 0)
            IssueMove(hx, hy);
    }

    void MoveCursor(int dx, int dy)
    {
        if (!KbCursor)
        {
            KbCursor = true;
            CurX = Selected?.X ?? Grid.W / 2;
            CurY = Selected?.Y ?? Grid.H / 2;
        }
        CurX = Util.Clamp(CurX + dx, 0, Grid.W - 1);
        CurY = Util.Clamp(CurY + dy, 0, Grid.H - 1);
    }

    // ---------------- camera + pause ----------------
    void HandleCamera()
    {
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            var mouse = Raylib.GetMousePosition();
            var before = Raylib.GetScreenToWorld2D(mouse, ViewCamera(false));
            CamZoom = Util.Clamp(CamZoom + wheel * 0.12f, 1f, 2.4f);
            var after = Raylib.GetScreenToWorld2D(mouse, ViewCamera(false));
            CamPan += before - after;                 // keep the point under the cursor anchored
        }
        if (Raylib.IsMouseButtonDown(MouseButton.Middle))
            CamPan -= Raylib.GetMouseDelta() / CamZoom;
        if (Raylib.IsKeyPressed(KeyboardKey.C)) { CamZoom = 1f; CamPan = Vector2.Zero; }

        if (CamZoom <= 1.001f) { CamZoom = 1f; CamPan = Vector2.Zero; }  // no pan when fully out
        else
        {
            CamPan.X = Util.Clamp(CamPan.X, -Cfg.BoardW * 0.5f, Cfg.BoardW * 0.5f);
            CamPan.Y = Util.Clamp(CamPan.Y, -Cfg.BoardH * 0.5f, Cfg.BoardH * 0.5f);
        }
    }

    void HandlePauseMenu()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseResume)) Paused = false;
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseFullscreen)) Display.ToggleFullscreen();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseWindow)) Display.CycleSize();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseMute)) Audio.ToggleMute();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseShake)) Fx.ShakeOn = !Fx.ShakeOn;
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseThreat)) ShowThreatPref = !ShowThreatPref;
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseAbandon)) { Paused = false; Phase = Phase.Lose; LoseTitle = "RUN ABANDONED"; LoseReason = "You called off the campaign."; Audio.Play("lose"); }
    }

    void DoAction(string id)
    {
        switch (id)
        {
            case "shoot": ToggleAim(); break;
            case "grenade": ToggleGrenade(); break;
            case "ability": DoAbility(); break;
            case "overwatch": DoOverwatch(); break;
            case "hunker": DoHunker(); break;
            case "hack": DoHack(); break;
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
        GrenadeMode = false;
        AimMode = true;
        AimTarget = FirstTargetFor(Selected);
    }

    void ToggleGrenade()
    {
        if (Selected == null || !Selected.CanAct || Selected.Grenades <= 0) return;
        GrenadeMode = !GrenadeMode;
        if (GrenadeMode) AimMode = false;
    }

    void IssueGrenade(int tx, int ty)
    {
        if (Selected == null || !Selected.CanAct || Selected.Grenades <= 0) return;
        if (Util.TileDist(Selected.X, Selected.Y, tx, ty) > GrenadeRange) return;
        Selected.Grenades--;
        Selected.ActionsLeft = 0;
        Enqueue(new GrenadeAnim(Selected, tx, ty), Team.Player);
        GrenadeMode = false;
    }

    void IssueMove(int tx, int ty)
    {
        if (MoveCost == null) return;
        int c = MoveCost[tx, ty];
        if (c <= 0) return;
        int need = c <= Selected.MoveBudget ? 1 : 2;
        bool blitz = Selected.Blitz;
        int cost = blitz ? Math.Max(0, need - 1) : need;   // Blitz: one action cheaper
        if (cost > Selected.ActionsLeft) return;
        var path = Grid.ReconstructPath(_cameFrom, Selected.X, Selected.Y, tx, ty);
        if (path.Count == 0) return;
        Selected.ActionsLeft -= cost;
        if (blitz) Selected.Blitz = false;
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
        // Run & Gun: this shot costs one action instead of ending the turn.
        if (Selected.RunGun) { Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1); Selected.RunGun = false; }
        else Selected.ActionsLeft = 0;
        var res = Combat.Resolve(Grid, Selected, target);
        Selected.Steady = false;                         // braced shot consumed
        Enqueue(new ShotAnim(Selected, target, res), Team.Player);
        if (!target.Active) ActivatePod(target.PodId);   // gunfire reveals the pod
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

    void DoHack()
    {
        if (!CanHack(Selected)) return;
        Selected.ActionsLeft -= 1;
        HackProgress++;
        var at = Util.TileCenter(Terminal.x, Terminal.y);
        Fx.PopText(at + new Vector2(0, -30), HackProgress >= HackRequired ? "HACKED" : "HACK +1", Pal.Accent, 20f);
        Fx.Burst(at, Pal.Accent, 14, 160f, 0.5f, 3f);
        Audio.Play("reload");
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

    // Whether the selected unit could fire its signature ability right now.
    public bool CanAbility(Unit u)
    {
        if (u == null || u.Team != Team.Player || !u.CanAct || u.AbilityCharge <= 0) return false;
        return u.Ability switch
        {
            AbilityKind.RunGun  => !u.RunGun,
            AbilityKind.Blitz   => !u.Blitz,
            AbilityKind.Steady  => !u.Steady && u.ActionsLeft >= 1,
            AbilityKind.Suppress=> u.Ammo > 0 && HasAnyTarget(u),
            _ => false,
        };
    }

    void DoAbility()
    {
        var u = Selected;
        if (!CanAbility(u)) return;
        var at = u.Pos + new Vector2(0, -34);
        switch (u.Ability)
        {
            case AbilityKind.RunGun:
                u.RunGun = true; u.AbilityCharge--;
                Fx.PopText(at, "RUN & GUN", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Blitz:
                u.Blitz = true; u.AbilityCharge--;
                Fx.PopText(at, "BLITZ", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Steady:
                u.Steady = true; u.AbilityCharge--; u.ActionsLeft -= 1;
                Fx.PopText(at, "STEADY", Pal.Good, 18f);
                Fx.Burst(u.Pos, Pal.Good, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Suppress:
                var t = FirstTargetFor(u);
                if (t == null) return;
                u.AbilityCharge--; u.Ammo--; u.ActionsLeft = 0; u.OnOverwatch = true;
                t.Suppress = Combat.SuppressAim;
                Fx.PopText(t.Pos + new Vector2(0, -34), "SUPPRESSED", Pal.Foe, 18f);
                Fx.PopText(at, "SUPPRESS", Pal.Accent, 16f);
                Audio.Play("over");
                if (!t.Active) ActivatePod(t.PodId);   // pinning fire reveals the pod
                break;
        }
        AimMode = false;
        GrenadeMode = false;
    }

    void RequestEndTurn()
    {
        if (!EndTurnArmed && AlivePlayers().Any(p => p.CanAct)) { EndTurnArmed = true; return; }
        EndTurnArmed = false;
        EndPlayerTurn();
    }

    void EndPlayerTurn()
    {
        EndTurnArmed = false;
        AimMode = false;
        Selected = null;
        MoveCost = null;
        Phase = Phase.EnemyTurn;
        foreach (var e in Enemies) if (e.Alive) e.BeginTurn();
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();  // dormant pods don't act
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
        if (AutoPlay) AutoStallCheck();
        foreach (var p in Players) if (p.Alive) p.BeginTurn();
        foreach (var e in Enemies) if (e.Alive) { e.ReactedThisTurn = false; e.Suppress = 0; } // OW resets; suppression expires
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
            // elite boss: enrage once when first acting below half HP
            if (e.Cls == "ELITE" && !e.Enraged && e.Hp <= e.MaxHp / 2)
            {
                e.Enraged = true;
                e.Aim += 15; e.Mobility += 2;
                Fx.PopText(e.Pos + new Vector2(0, -34), "ENRAGED", Pal.Foe, 20f);
                Fx.AddShake(8f);
                ShowBanner("WARLORD ENRAGED", true);
            }
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
                if (_aiPlan.HealTarget != null && _aiPlan.HealTarget.Alive && e.ActionsLeft > 0 &&
                    _aiPlan.HealTarget.Hp < _aiPlan.HealTarget.MaxHp &&
                    Util.TileDist(e.X, e.Y, _aiPlan.HealTarget.X, _aiPlan.HealTarget.Y) <= Ai.HealRange &&
                    Grid.HasLineOfSight(e.X, e.Y, _aiPlan.HealTarget.X, _aiPlan.HealTarget.Y))
                {
                    e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "MEDIC", Pal.Good, 16f);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new HealAnim(e, _aiPlan.HealTarget), Team.Enemy);
                }
                else if (_aiPlan.Grenade && e.Grenades > 0 && e.ActionsLeft > 0 &&
                    Util.TileDist(e.X, e.Y, _aiPlan.GrenX, _aiPlan.GrenY) <= GrenadeRange)
                {
                    e.Grenades--;
                    e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "FRAG OUT", Pal.Foe, 16f);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new GrenadeAnim(e, _aiPlan.GrenX, _aiPlan.GrenY), Team.Enemy);
                }
                else if (_aiPlan.ShootTarget != null && _aiPlan.ShootTarget.Alive &&
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

    // ---------------- barracks perk choice ----------------
    void ChoosePerk(int which)
    {
        if (_run.PendingPerks.Count == 0) return;
        var off = _run.PendingPerks[0];
        Perk p = which == 0 ? off.A : off.B;
        Run.ApplyPerk(off.Unit, p);
        _run.Report.Add($"{off.Unit.Name} gains {PerkDef.Name(p)}");
        _run.PendingPerks.RemoveAt(0);
        Audio.Play("select");
    }

    void HandlePerkClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        if (Raylib.CheckCollisionPointRec(m, Hud.PerkTagBtn) && _run.PendingPerks.Count > 0)
            OpenTagEditor(_run.PendingPerks[0].Unit);
        else if (Raylib.CheckCollisionPointRec(m, Hud.PerkBtnA)) ChoosePerk(0);
        else if (Raylib.CheckCollisionPointRec(m, Hud.PerkBtnB)) ChoosePerk(1);
    }

    // ---------------- custom tag editor ----------------
    void OpenTagEditor(Unit u)
    {
        if (u == null || u.Team != Team.Player || u.IsVip) return;
        EditingTag = true;
        TagTarget = u;
        TagBuffer = u.CustomTag ?? "";
    }

    void UpdateTagEditor()
    {
        if (TagTarget == null) { EditingTag = false; return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { EditingTag = false; return; }   // cancel
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            TagTarget.CustomTag = TagBuffer.Trim();   // empty string clears -> auto tags return
            EditingTag = false;
            Audio.Play("select");
            return;
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
            && TagBuffer.Length > 0)
            TagBuffer = TagBuffer.Substring(0, TagBuffer.Length - 1);
        int ch = Raylib.GetCharPressed();
        while (ch > 0)
        {
            if (TagBuffer.Length < 14 && ch >= 32 && ch < 127)
                TagBuffer += char.ToUpper((char)ch);   // ASCII-only, uppercase to match the UI font
            ch = Raylib.GetCharPressed();
        }
    }

    // ---------------- barracks requisition shop ----------------
    /// True if item i can currently be bought (affordable + has an effect).
    public bool CanBuy(int item)
    {
        if (item < 0 || item >= ShopCost.Length || _run.Intel < ShopCost[item]) return false;
        return item switch
        {
            0 => _run.Squad.Any(u => u.Hp < u.MaxHp),     // medkit needs a wounded soldier
            2 => _run.Squad.Any(u => CountPerksLeft(u) >= 2), // training needs an un-maxed soldier
            3 => _run.Squad.Any(u => u.BonusGrenades < 2),    // cache caps at +2 per soldier
            _ => _run.Squad.Count > 0,
        };
    }

    static int CountPerksLeft(Unit u)
    {
        int c = 0;
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) c++;
        return c;
    }

    /// The soldier a purchase would affect (for the shop preview). Matches DoPurchase.
    public Unit ShopTarget(int item) => item switch
    {
        0 => _run.Squad.Where(u => u.Hp < u.MaxHp).OrderBy(u => u.Hp).FirstOrDefault(),
        1 => _run.Squad.OrderBy(u => u.MaxHp).FirstOrDefault(),
        3 => _run.Squad.Where(u => u.BonusGrenades < 2).OrderBy(u => u.BonusGrenades).FirstOrDefault(),
        _ => null,
    };

    /// One-line concrete effect of a purchase, so the player can judge its value.
    public string ShopEffect(int item)
    {
        var t = ShopTarget(item);
        switch (item)
        {
            case 0: return t == null ? "no one is wounded" : $"{t.Name}: {t.Hp} -> {t.MaxHp} HP  (+{t.MaxHp - t.Hp})";
            case 1: return t == null ? "-" : $"{t.Name}: max HP {t.MaxHp} -> {t.MaxHp + 2}";
            case 2: return _run.Squad.Any(u => CountPerksLeft(u) >= 2) ? "a soldier gains a perk pick" : "every soldier is maxed";
            case 3: return t == null ? "all soldiers at the cap" : $"{t.Name}: +1 grenade/mission";
            default: return "";
        }
    }

    void DoPurchase(int item)
    {
        if (!CanBuy(item)) { Audio.Play("miss"); return; }
        switch (item)
        {
            case 0:
                var hurt = _run.Squad.Where(u => u.Hp < u.MaxHp).OrderBy(u => u.Hp).First();
                int amt = hurt.MaxHp - hurt.Hp; hurt.Hp = hurt.MaxHp;
                _run.Report.Add($"{hurt.Name} field-treated  (+{amt} HP)");
                break;
            case 1:
                var weak = _run.Squad.OrderBy(u => u.MaxHp).First();
                weak.MaxHp += 2; weak.Hp += 2;
                _run.Report.Add($"{weak.Name} stimmed  (+2 max HP)");
                break;
            case 2:
                if (!_run.TryQueueBonusPerk("requisition")) { Audio.Play("miss"); return; }
                break;
            case 3:
                var carrier = _run.Squad.Where(u => u.BonusGrenades < 2).OrderBy(u => u.BonusGrenades).First();
                carrier.BonusGrenades += 1;
                _run.Report.Add($"{carrier.Name} issued a frag cache  (+1 grenade/mission)");
                break;
        }
        _run.Intel -= ShopCost[item];
        Audio.Play("select");
    }

    void HandleShopClick()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { _shopDone = true; Audio.Play("turn"); return; }
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        for (int i = 0; i < Hud.ShopBtns.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.ShopBtns[i])) { DoPurchase(i); return; }
        if (Raylib.CheckCollisionPointRec(m, Hud.ShopProceed)) { _shopDone = true; Audio.Play("turn"); }
    }

    // autopilot: buy a medkit if it helps, then move on (keeps the shop path covered)
    void AutoShop()
    {
        if (CanBuy(0)) DoPurchase(0);
        _shopDone = true;
    }

    /// Harness hook (screenshot only): open the custom-tag editor on a soldier.
    public void DebugTagEditor()
    {
        TagTarget = Players.FirstOrDefault(p => !p.IsVip) ?? (_run.Squad.Count > 0 ? _run.Squad[0] : null);
        if (TagTarget == null) return;
        TagBuffer = "BREACHER";
        EditingTag = true;
    }

    /// Harness hook (screenshot only): show the barracks requisition shop.
    public void DebugShop()
    {
        if (_run.Squad.Count == 0) { _run = new Run(); _run.Start(); }
        _run.Intel = 24;
        _run.DebriefSurvivors();
        if (_run.Squad.Count > 0) _run.Squad[0].Hp = Math.Max(1, _run.Squad[0].Hp - 4);  // wound for the medkit demo
        _run.Report.Insert(0, $"Recovered 17 intel  (total {_run.Intel})");
        _shopDone = false;
        Phase = Phase.Barracks;
    }

    /// Headless self-test (SIGHTLINE_DEATHTEST): kill the whole squad on an escort
    /// mission (VIP solos to evac) and confirm the dead, leveled soldiers do NOT carry
    /// into the next mission. Returns a one-line report.
    public string DeathConsequenceTest()
    {
        NoPersist = true;
        _run = new Run(); _run.Start();
        // give one soldier a rank/perk so we'd notice if a "leveled" unit survived death
        _run.Squad[0].Rank = 2; _run.Squad[0].Perks.Add(Perk.Deadeye);
        var before = _run.Squad.Select(u => u.Name).ToList();
        _run.CurrentCard = new MissionCard { Objective = Objective.Escort, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(1);
        foreach (var u in Players.Where(p => !p.IsVip).ToList()) { u.Hp = 0; KillUnit(u); }
        int stillAlive = AlivePlayers().Count(p => !p.IsVip);
        Vip.X = EvacZone[0].x; Vip.Y = EvacZone[0].y;     // VIP reaches extraction -> escort win
        CheckEnd();                                        // -> EnterBarracks
        var after = _run.Squad.Select(u => u.Name).ToList();
        int carried = after.Count(n => before.Contains(n));
        string verdict = (stillAlive == 0 && carried == 0) ? "PASS" : "FAIL";
        return $"DEATHTEST: {verdict} | soldiersAliveAfterKill={stillAlive} phase={Phase} " +
               $"fallen={_run.Fallen.Count} before=[{string.Join(",", before)}] after=[{string.Join(",", after)}]";
    }

    void ChooseCard(int i)
    {
        if (i < 0 || i >= _run.Offers.Count) return;
        _run.CurrentCard = _run.Offers[i];
        Audio.Play("select");
        NextMission();
    }

    void HandleCardClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        for (int i = 0; i < Hud.MissionCards.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.MissionCards[i])) { ChooseCard(i); return; }
    }

    // ---------------- overlay click ----------------
    void HandleOverlayClick()
    {
        // intro CONTINUE: resume a saved campaign (button or key C)
        if (Phase == Phase.Intro && SaveGame.Exists)
        {
            bool cont = (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                         Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn2))
                        || Raylib.IsKeyPressed(KeyboardKey.C);
            if (cont && ContinueRun()) return;
        }

        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn);
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter);
        if (!click && !enter) return;

        if (Phase == Phase.Barracks) NextMission();   // deploy to next mission
        else StartMission();                           // intro / win / lose -> new run
    }

    // ---------------- draw ----------------
    Vector2 BoardCenter => new(Cfg.OriginX + Cfg.BoardW / 2f, Cfg.OriginY + Cfg.BoardH / 2f);

    /// The board camera. withShake folds in the transient screen-shake + zoom-punch
    /// (visual only); the picking variant omits them so mouse->tile stays stable.
    public Camera2D ViewCamera(bool withShake)
    {
        var bc = BoardCenter;
        return new Camera2D
        {
            Target = bc + CamPan,
            Offset = bc + (withShake ? Fx.ShakeOffset : Vector2.Zero),
            Rotation = 0f,
            Zoom = CamZoom * (withShake ? (1f + _camPulse) : 1f),
        };
    }

    public void Draw()
    {
        Raylib.ClearBackground(Pal.Bg);

        Raylib.BeginMode2D(ViewCamera(true));
        Renderer.DrawBoard(this);
        Raylib.EndMode2D();

        Hud.Draw(this);
    }
}
