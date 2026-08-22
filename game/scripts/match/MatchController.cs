using System;
using Godot;
using Snookering.Core.Physics;
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
    private enum Mode { Aiming, Striking, Simulating, Playback }

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

    // Camera rig: orbits the TABLE, not the cue ball.
    private static readonly Vector3 TableFocus = new(0f, 0.1f, 0f);
    private float _yaw = -Mathf.Pi / 2f; // behind the baulk end (cue ball side), looking at the rack
    private float _pitch = 0.85f;
    private float _dist = 2.7f;
    private bool _orbiting;

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

    public override void _Ready()
    {
        _table = TableSpec.Pool9ft();
        AddChild(TableBuilder.Build(_table));

        _state = Racks.EightBall(_table);
        _views = new BallView[_state.Balls.Length];
        for (var i = 0; i < _state.Balls.Length; i++)
        {
            _views[i] = BallView.Create(_state.Balls[i].Id, (float)_table.Physics.R);
            AddChild(_views[i]);
        }
        SnapViews();

        _camera = new Camera3D { Name = "MatchCamera", Fov = 55f };
        AddChild(_camera);
        _camera.MakeCurrent();

        _cue = CueView.Create();
        AddChild(_cue);

        BuildHud();
        UpdateCamera();

        // CLI test hook: "--break [power01]" fires a break immediately (harness verification).
        var args = OS.GetCmdlineUserArgs();
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
                    _aiming = mb.Pressed && _mode == Mode.Aiming;
                    SetMouseCaptured(mb.Pressed && _aiming);
                }
                else if (mb.ButtonIndex == MouseButton.Right)
                {
                    _orbiting = mb.Pressed;
                    SetMouseCaptured(mb.Pressed);
                }
                else if (mb.ButtonIndex == MouseButton.WheelUp)
                    _dist = Mathf.Clamp(_dist * 0.92f, 0.6f, 4.5f);
                else if (mb.ButtonIndex == MouseButton.WheelDown)
                    _dist = Mathf.Clamp(_dist * 1.08f, 0.6f, 4.5f);
                break;

            case InputEventMouseMotion mm:
                if (_orbiting)
                {
                    _yaw -= mm.Relative.X * 0.005f;
                    _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.005f, 0.15f, 1.5f);
                }
                else if (_aiming)
                {
                    // Slower when zoomed in for fine aiming.
                    _aimAngle -= mm.Relative.X * 0.0025f * Mathf.Clamp(_dist / 2.7f, 0.35f, 1f);
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
            case Key.Space when _mode == Mode.Aiming:
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
                _state = Racks.EightBall(_table);
                _result = null;
                _simTask = null;
                _mode = Mode.Aiming;
                _aimAngle = 0f;
                SetSpin(0f, 0f);
                SnapViews();
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
        _result = null;
        _mode = Mode.Aiming;

        // Gray-box ball-in-hand: a pocketed cue ball returns to the head spot (rules arrive in M2).
        ref var cue = ref _state.Ball(0);
        if (!cue.OnTable)
        {
            var spot = Racks.HeadSpot(_table);
            while (Occupied(spot))
                spot = new CoreVec2(spot.X + 2.5 * _table.Physics.R, spot.Y);
            cue = BallState.AtRest(0, spot);
        }
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
        }

        UpdateCamera();
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

    private void UpdateCamera()
    {
        var offset = new Vector3(
            Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _dist;
        _camera.Position = TableFocus + offset;
        _camera.LookAt(TableFocus, Vector3.Up);
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
        _info.Text = _mode switch
        {
            Mode.Aiming => $"AIM   spin side={_spinSide:F2} vert={_spinVert:F2}   [LMB] aim  [RMB] orbit  [wheel] zoom  [arrows] spin  [Space] power  [R] re-rack",
            Mode.Striking or Mode.Simulating => "STRIKE",
            _ => "SHOT ...",
        };
    }
}
