using System;
using Godot;
using Snookering.Core.Physics;
using Snookering.Core.Rules;
using Snookering.Core.Tables;
using Snookering.Game.Render;
using CoreVec2 = Snookering.Core.Mathematics.Vec2;

namespace Snookering.Game.Match;

/// <summary>
/// Gray-box match scene driver (M1): owns the Core table state, converts input
/// into ShotInput, runs the deterministic simulator on a worker thread, and plays
/// back the returned trajectory. NO physics or rules logic lives here.
///
/// Controls: LMB-drag aim (rotates the cue) · RMB-drag orbit camera · wheel zoom ·
/// arrows spin · hold/release Space = power · R = re-rack.
/// </summary>
public partial class MatchController : Node3D
{
    private enum Mode { Aiming, Striking, Simulating, Playback, BallInHand }

    public enum GameType { EightBall, Snooker }

    private TableSpec _table = null!;
    private TableState _state = null!;
    private BallView[] _views = null!;
    private Camera3D _camera = null!;
    private CueView _cue = null!;
    private Label _info = null!;
    private ColorRect _powerFill = null!;

    private Mode _mode = Mode.Aiming;

    // Aim state (sim-plane radians; 0 points down the table toward the rack).
    private float _aimAngle;
    private bool _aiming;

    // Two-camera scheme:
    //  - Aiming: anchored low behind the cue ball, swinging with the aim (first-person-ish).
    //  - Playback: free orbit around the table so the whole shot is visible.
    private static readonly Vector3 TableFocus = new(0f, 0.1f, 0f);
    private float _yaw = -Mathf.Pi / 2f; // playback orbit: behind the baulk end
    private float _pitch = 0.85f;
    private float _dist = 2.7f;
    private float _aimDist = 0.6f;
    private float _aimPitch = 0.30f; // low over the cue
    private bool _orbiting;
    private Vector3 _camPos;
    private Vector3 _camLook;
    private bool _camInitialized;

    // Shot input state.
    private float _spinSide;
    private float _spinVert;
    private float _power;
    private bool _charging;

    // Strike animation + simulation/playback state.
    private float _strikeT;
    private float _firedPower;
    private System.Threading.Tasks.Task<ShotResult>? _simTask;
    private ShotResult? _result;
    private double _playTime;
    private TableState? _preShotState;

    // Rules.
    private GameType _gameType = GameType.EightBall;
    private readonly EightBallRules _rules = new();
    private EightBallGame _game = new();
    private SnookerRules? _snookerRules;
    private SnookerGame _snookerGame = new();
    private string _message = "Player 1 to break";

    // Ball-in-hand placement (restrictedToD = snooker in-hand).
    private CoreVec2 _placement;
    private bool _placementValid;
    private bool _placementInD;

    private Node3D? _tableNode;

    public override void _Ready()
    {
        _camera = new Camera3D { Name = "MatchCamera", Fov = 55f };
        AddChild(_camera);
        _camera.MakeCurrent();

        _cue = CueView.Create();
        AddChild(_cue);

        BuildHud();

        // CLI test hooks: "--game snooker|8ball" selects the game,
        // "--break [power01]" fires a break immediately (harness verification).
        var args = OS.GetCmdlineUserArgs();
        var startType = GameType.EightBall;
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--game" && i + 1 < args.Length && args[i + 1] == "snooker")
                startType = GameType.Snooker;

        StartRack(startType);
        UpdateCamera(0f);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--break")
            {
                _power = args.Length > i + 1 && float.TryParse(args[i + 1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var p)
                    ? Mathf.Clamp(p, 0f, 1f)
                    : 1f;
                Fire();
                break;
            }
        }
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb:
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (_mode == Mode.BallInHand)
                    {
                        if (mb.Pressed && _placementValid)
                            ConfirmPlacement();
                    }
                    else
                    {
                        _aiming = mb.Pressed && _mode == Mode.Aiming;
                        SetMouseCaptured(mb.Pressed && _aiming);
                    }
                }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    _orbiting = mb.Pressed;
                    SetMouseCaptured(mb.Pressed);
                }
                else if (mb.ButtonIndex == MouseButton.WheelUp)
                {
                    if (InAimView)
                        _aimDist = Mathf.Clamp(_aimDist * 0.92f, 0.25f, 1.6f);
                    else
                        _dist = Mathf.Clamp(_dist * 0.92f, 0.6f, 4.5f);
                }
                else if (mb.ButtonIndex == MouseButton.WheelDown)
                {
                    if (InAimView)
                        _aimDist = Mathf.Clamp(_aimDist * 1.08f, 0.25f, 1.6f);
                    else
                        _dist = Mathf.Clamp(_dist * 1.08f, 0.6f, 4.5f);
                }
                break;

            case InputEventMouseMotion mm:
                if (_aiming && InAimView)
                {
                    // The camera hangs behind the cue, so turning the aim IS turning the view.
                    _aimAngle -= mm.Relative.X * 0.002f * Mathf.Clamp(_aimDist / 0.6f, 0.4f, 1f);
                    _aimPitch = Mathf.Clamp(_aimPitch + mm.Relative.Y * 0.003f, 0.10f, 1.1f);
                }
                else if (_orbiting)
                {
                    if (InAimView)
                    {
                        _aimAngle -= mm.Relative.X * 0.002f;
                        _aimPitch = Mathf.Clamp(_aimPitch + mm.Relative.Y * 0.003f, 0.10f, 1.1f);
                    }
                    else
                    {
                        _yaw -= mm.Relative.X * 0.005f;
                        _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.005f, 0.15f, 1.5f);
                    }
                }
                break;

            case InputEventKey key when key.Pressed && !key.Echo:
                HandleKey(key.Keycode);
                break;

            case InputEventKey key when !key.Pressed && key.Keycode == Key.Space && _charging && _mode == Mode.Aiming:
                _charging = false;
                Fire();
                break;
        }
    }

    private void SetMouseCaptured(bool captured) =>
        Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;

    private void HandleKey(Key code)
    {
        const float step = 0.05f;
        switch (code)
        {
            case Key.Space when _mode == Mode.Aiming && !(_gameType == GameType.EightBall ? _game.GameOver : _snookerGame.FrameOver):
                _charging = true;
                _power = 0f;
                break;
            case Key.Left:
                SetSpin(_spinSide + step, _spinVert);
                break;
            case Key.Right:
                SetSpin(_spinSide - step, _spinVert);
                break;
            case Key.Up:
                SetSpin(_spinSide, _spinVert + step);
                break;
            case Key.Down:
                SetSpin(_spinSide, _spinVert - step);
                break;
            case Key.R:
                StartRack(_gameType);
                break;
            case Key.G when _mode is Mode.Aiming or Mode.BallInHand:
                StartRack(_gameType == GameType.EightBall ? GameType.Snooker : GameType.EightBall);
                break;
        }
    }

    private void SetSpin(float side, float vert)
    {
        var len = Mathf.Sqrt(side * side + vert * vert);
        const float max = 0.45f;
        if (len > max)
        {
            side *= max / len;
            vert *= max / len;
        }
        _spinSide = side;
        _spinVert = vert;
    }

    /// <summary>(Re)build table, balls, and match state for the chosen game.</summary>
    private void StartRack(GameType type)
    {
        var winnerBreaks = type == _gameType
            ? _gameType == GameType.EightBall
                ? (_game.GameOver && _game.Winner >= 0 ? _game.Winner : 0)
                : (_snookerGame.FrameOver && _snookerGame.Winner >= 0 ? _snookerGame.Winner : 0)
            : 0;

        _gameType = type;
        _table = type == GameType.EightBall ? TableSpec.Pool9ft() : TableSpec.Snooker12ft();
        _snookerRules = type == GameType.Snooker ? new SnookerRules(_table) : null;
        _game = new EightBallGame { CurrentPlayer = winnerBreaks };
        _snookerGame = new SnookerGame { CurrentPlayer = winnerBreaks };
        _message = $"{(type == GameType.EightBall ? "8-Ball" : "Snooker")} — Player {winnerBreaks + 1} to break";

        _tableNode?.QueueFree();
        _tableNode = TableBuilder.Build(_table);
        AddChild(_tableNode);

        if (_views is not null)
            foreach (var v in _views)
                v.QueueFree();

        _state = type == GameType.EightBall ? Racks.EightBall(_table) : Racks.Snooker(_table);
        _views = new BallView[_state.Balls.Length];
        for (var i = 0; i < _state.Balls.Length; i++)
        {
            var id = _state.Balls[i].Id;
            var color = type == GameType.EightBall ? BallView.PoolColor(id) : BallView.SnookerColor(id);
            _views[i] = BallView.Create(id, (float)_table.Physics.R, color);
            AddChild(_views[i]);
        }
        SnapViews();

        _result = null;
        _simTask = null;
        _mode = Mode.Aiming;
        _aimAngle = 0f;
        SetSpin(0f, 0f);
    }

    // ------------------------------------------------------------------ shot flow

    private void Fire()
    {
        var speed = 0.4 + _power * 6.6; // 0.4–7.0 m/s
        var shot = new ShotInput
        {
            AimAngleMicroRad = (int)Math.Round((double)_aimAngle * 1e6),
            SpeedMmPerSec = (int)Math.Round(speed * 1e3),
            OffsetSide1e4 = (short)Math.Round(_spinSide * 1e4),
            OffsetVert1e4 = (short)Math.Round(_spinVert * 1e4),
            ElevationCentiDeg = 0,
        };

        _preShotState = _state;
        var state = _state;
        var table = _table;
        _simTask = System.Threading.Tasks.Task.Run(() => Simulator.Run(state, shot, table));

        _firedPower = _power;
        _strikeT = 0f;
        _mode = Mode.Striking;
        _power = 0f;
    }

    private void FinishPlayback()
    {
        _state = _result!.FinalState;

        bool ballInHand, inD = false, over;
        if (_gameType == GameType.EightBall)
        {
            var outcome = _rules.Apply(_game, _preShotState!, _result);
            _message = outcome.Message;
            ballInHand = outcome.BallInHand;
            over = outcome.GameOver;
        }
        else
        {
            var outcome = _snookerRules!.Apply(_snookerGame, _preShotState!, _result);
            _message = outcome.Message;
            ballInHand = outcome.BallInHandInD;
            inD = ballInHand;
            over = outcome.FrameOver;
        }
        _result = null;

        if (!over && ballInHand)
            EnterBallInHand(inD);
        else
            _mode = Mode.Aiming;
        SnapViews();
    }

    // ------------------------------------------------------------------ ball in hand

    private void EnterBallInHand(bool restrictedToD)
    {
        _mode = Mode.BallInHand;
        _placementInD = restrictedToD;
        SetMouseCaptured(false);

        _placement = restrictedToD
            ? new CoreVec2(_table.Snooker!.BaulkX - 0.15, 0.0)
            : Racks.HeadSpot(_table);
        while (Occupied(_placement))
            _placement = new CoreVec2(_placement.X + 2.5 * _table.Physics.R, _placement.Y);
        _placementValid = true;
    }

    private void UpdatePlacement()
    {
        var mouse = GetViewport().GetMousePosition();
        var from = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);
        var r = (float)_table.Physics.R;

        if (Mathf.Abs(dir.Y) > 1e-4f)
        {
            var t = (r - from.Y) / dir.Y;
            if (t > 0f)
            {
                var hit = from + dir * t;
                var sim = SimWorld.ToSim(hit);
                _placement = new CoreVec2(
                    Math.Clamp(sim.X, -_table.HalfLength + _table.Physics.R, _table.HalfLength - _table.Physics.R),
                    Math.Clamp(sim.Y, -_table.HalfWidth + _table.Physics.R, _table.HalfWidth - _table.Physics.R));
            }
        }

        _placementValid = !Occupied(_placement) && (!_placementInD || InsideD(_placement));

        foreach (var v in _views)
        {
            if (v.BallId == 0)
            {
                v.Visible = true;
                v.Position = SimWorld.ToWorld(_placement, r);
                v.SetBaseColor(_placementValid ? new Color(0.95f, 0.93f, 0.88f) : new Color(0.9f, 0.25f, 0.2f));
            }
        }
    }

    private bool InsideD(CoreVec2 pos)
    {
        var d = _table.Snooker!;
        return pos.X <= d.BaulkX + 1e-9 && (pos - d.DCenter).Length <= d.DRadiusValue + 1e-9;
    }

    private void ConfirmPlacement()
    {
        ref var cue = ref _state.Ball(0);
        cue = BallState.AtRest(0, _placement);
        foreach (var v in _views)
            if (v.BallId == 0)
                v.SetBaseColor(new Color(0.95f, 0.93f, 0.88f));
        _mode = Mode.Aiming;
        SnapViews();
    }

    private bool Occupied(CoreVec2 pos)
    {
        foreach (var b in _state.Balls)
            if (b.OnTable && b.Id != 0 && (b.Pos - pos).Length < 2.0 * _table.Physics.R)
                return true;
        return false;
    }

    // ------------------------------------------------------------------ per-frame

    public override void _Process(double delta)
    {
        if (_charging)
            _power = Mathf.PingPong((float)(_power + delta * 0.9), 1f);

        switch (_mode)
        {
            case Mode.Striking:
                _strikeT += (float)delta / 0.11f;
                if (_strikeT >= 1f)
                    _mode = Mode.Simulating;
                break;

            case Mode.Simulating when _simTask is not null && _simTask.IsCompleted:
                if (_simTask.IsCompletedSuccessfully)
                {
                    _result = _simTask.Result;
                    _playTime = 0.0;
                    _mode = Mode.Playback;
                }
                else
                {
                    GD.PrintErr($"[match] simulation failed: {_simTask.Exception?.GetBaseException().Message}");
                    _mode = Mode.Aiming;
                }
                _simTask = null;
                break;

            case Mode.Playback when _result is not null:
                _playTime += delta;
                ApplyPlayback((float)delta);
                if (_playTime >= _result.Duration + 0.3)
                    FinishPlayback();
                break;

            case Mode.BallInHand:
                UpdatePlacement();
                break;
        }

        UpdateCamera((float)delta);
        UpdateCue();
        UpdateHud();
    }

    private void ApplyPlayback(float dt)
    {
        var frames = _result!.Frames;
        var interval = Simulator.Dt * Simulator.TrajectorySampleEveryTicks;
        var k = Math.Min((int)(_playTime / interval), frames.Count - 1);
        var next = Math.Min(k + 1, frames.Count - 1);
        var alpha = interval > 0.0 ? Mathf.Clamp((float)((_playTime - k * interval) / interval), 0f, 1f) : 0f;

        for (var i = 0; i < _views.Length; i++)
        {
            var a = frames[k].Balls[i];
            var b = frames[next].Balls[i];
            var lerped = new BallSample(
                a.Id,
                new CoreVec2(
                    a.Pos.X + (b.Pos.X - a.Pos.X) * alpha,
                    a.Pos.Y + (b.Pos.Y - a.Pos.Y) * alpha),
                a.AngVel,
                a.OnTable && b.OnTable);
            _views[i].Apply(in lerped, dt);
        }
    }

    private void SnapViews()
    {
        for (var i = 0; i < _views.Length; i++)
        {
            var b = _state.Balls[i];
            _views[i].Snap(new BallSample(b.Id, b.Pos, b.AngVel, b.OnTable));
        }
    }

    private Vector3 CueBallPosition()
    {
        foreach (var v in _views)
            if (v.BallId == 0)
                return v.Position;
        return Vector3.Zero;
    }

    private bool InAimView => _mode is Mode.Aiming or Mode.Striking;

    private void UpdateCamera(float dt)
    {
        Vector3 targetPos, targetLook;

        if (InAimView)
        {
            // Low behind the cue ball, looking down the aim line.
            var ball = CueBallPosition();
            var dir = new Vector3(Mathf.Cos(_aimAngle), 0f, -Mathf.Sin(_aimAngle));
            targetPos = ball
                        - dir * (_aimDist * Mathf.Cos(_aimPitch))
                        + Vector3.Up * (_aimDist * Mathf.Sin(_aimPitch));
            targetLook = ball + dir * 0.6f;
        }
        else
        {
            var offset = new Vector3(
                Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
                Mathf.Sin(_pitch),
                Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _dist;
            targetPos = TableFocus + offset;
            targetLook = TableFocus;
        }

        if (!_camInitialized)
        {
            _camPos = targetPos;
            _camLook = targetLook;
            _camInitialized = true;
        }
        else
        {
            // Exponential smoothing: fast enough to feel direct, soft on mode switches.
            var k = 1f - Mathf.Exp(-12f * dt);
            _camPos = _camPos.Lerp(targetPos, k);
            _camLook = _camLook.Lerp(targetLook, k);
        }

        _camera.Position = _camPos;
        _camera.LookAt(_camLook, Vector3.Up);
    }

    private void UpdateCue()
    {
        var aimingPhase = _mode is Mode.Aiming or Mode.Striking;
        _cue.Visible = aimingPhase;
        if (!aimingPhase)
            return;

        var strike = _mode == Mode.Striking ? Mathf.Clamp(_strikeT, 0f, 1f) : 0f;
        var pull = _mode == Mode.Striking ? _firedPower : _power;
        _cue.Place(
            CueBallPosition(),
            (float)_table.Physics.R,
            _aimAngle,
            _spinSide,
            _spinVert,
            pull,
            strike);
    }

    // ------------------------------------------------------------------ HUD

    private void BuildHud()
    {
        var hud = new CanvasLayer { Name = "Hud" };
        AddChild(hud);

        _info = new Label
        {
            Position = new Vector2(16, 12),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _info.AddThemeColorOverride("font_color", Colors.White);
        hud.AddChild(_info);

        var barBack = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.5f),
            AnchorLeft = 0.3f,
            AnchorRight = 0.7f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -36,
            OffsetBottom = -16,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hud.AddChild(barBack);

        _powerFill = new ColorRect
        {
            Color = new Color(0.95f, 0.55f, 0.1f),
            AnchorRight = 0f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        barBack.AddChild(_powerFill);
    }

    private void UpdateHud()
    {
        _powerFill.AnchorRight = _power;

        string players;
        bool over;
        if (_gameType == GameType.EightBall)
        {
            string GroupName(int p) => _game.GroupOf(p) switch
            {
                BallGroup.Solids => "solids",
                BallGroup.Stripes => "stripes",
                _ => "?",
            };
            players = _game.OpenTable
                ? $"8-BALL — open table — Player {_game.CurrentPlayer + 1}'s turn"
                : $"8-BALL — P1 ({GroupName(0)}) vs P2 ({GroupName(1)}) — Player {_game.CurrentPlayer + 1}'s turn";
            over = _game.GameOver;
        }
        else
        {
            var g = _snookerGame;
            var target = g.ColorsPhase
                ? $"on the {SnookerRules.ColorName(g.NextColorOn)}"
                : g.ColorBallOn ? "on a color" : "on a red";
            players = $"SNOOKER — P1 {g.Scores[0]} : {g.Scores[1]} P2 — Player {g.CurrentPlayer + 1} {target}";
            over = g.FrameOver;
        }

        var status = _mode switch
        {
            _ when over => $"GAME OVER — {_message}   [R] next rack   [G] switch game",
            Mode.Aiming => $"{_message}   |   spin side={_spinSide:F2} vert={_spinVert:F2}   [LMB drag] aim  [wheel] zoom  [arrows] spin  [Space] power  [R] re-rack  [G] switch game",
            Mode.BallInHand => $"{_message} — move the mouse to place the cue ball, click to confirm",
            Mode.Striking or Mode.Simulating => "STRIKE",
            _ => "...",
        };

        _info.Text = $"{players}\n{status}";
    }
}
