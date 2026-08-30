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
    private Ui.Hud _hud = null!;
    private Ui.MenuLayer _menu = null!;
    private Net.NetworkManager _net = null!;

    // Ball in hand travels inside the shot, so the placement made before firing is
    // held here until the shot is built.
    private CoreVec2? _pendingPlacement;

    private Mode _mode = Mode.Aiming;

    // Aim state (sim-plane radians; 0 points down the table toward the rack).
    private float _aimAngle;
    private bool _aiming;

    // Two-camera scheme:
    //  - Aiming: anchored low behind the cue ball, swinging with the aim (first-person-ish).
    //  - Playback: free orbit around the table so the whole shot is visible.
    private static readonly Vector3 TableFocus = new(0f, 0.1f, 0f);
    private float _yaw = -Mathf.Pi / 2f; // playback orbit: behind the baulk end
    private float _pitch = 0.55f; // shot view stays low so the lamps are not in the way
    private float _dist = 2.7f;
    private float _aimDist = 0.6f;
    private float _aimPitch = 0.30f; // low over the cue (>= MinAimPitch)

    // Camera limits, so no view can end up inside the cue, the table or a lamp.
    // The cue rises from the tip at CueView.ElevationRad, so the aim camera has to
    // stay steeper than that to remain above it at every zoom distance.
    private const float MinAimPitch = 0.22f;   // > cue elevation (0.10) + margin
    private const float MaxAimPitch = 0.95f;
    private const float MinEyeHeight = 0.145f; // above the cloth and the cue butt
    private const float MaxEyeHeight = 1.05f;  // below the pendant lamps

    private const float MinOrbitPitch = 0.28f;
    private const float MaxOrbitPitch = 1.45f;
    private const float MinOrbitHeight = 0.30f; // never sinks into the table
    private bool _orbiting;
    private Vector3 _camPos;
    private Vector3 _camLook;
    private bool _camInitialized;
    private float _menuYaw = -Mathf.Pi / 2f;

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
    private int _snookerBreak;
    private string _message = "Player 1 to break";

    // Ball-in-hand placement (restrictedToD = snooker in-hand).
    private CoreVec2 _placement;
    private bool _placementValid;
    private bool _placementInD;

    private float _aimBroadcast;
    private float _spectatedElevation;

    private ulong _lastPhysicsHash;
    private ulong _lastRulesHash;
    private int _shotIndex;
    private (int Shot, ulong Physics, ulong Rules)? _pendingReport;

    private Node3D? _tableNode;
    private AudioManager _audio = null!;
    private int _nextEventIdx;

    // AI opponent (Player 2). 0 = human, 1..3 = Easy/Medium/Hard.
    private int _aiLevel;
    private System.Threading.Tasks.Task<Snookering.Core.Ai.AiShot>? _aiTask;
    private ShotInput? _aiPending;
    private float _aiThink;
    private ulong _aiSeed = 0x5EED;

    private int CurrentPlayer => _gameType == GameType.EightBall ? _game.CurrentPlayer : _snookerGame.CurrentPlayer;
    private bool IsGameOver => _gameType == GameType.EightBall ? _game.GameOver : _snookerGame.FrameOver;
    private bool AiTurn => _aiLevel > 0 && CurrentPlayer == 1 && !IsGameOver;

    /// <summary>
    /// True when someone else owns the table — the AI, or the remote player in an
    /// online match. Both cases suppress exactly the same local input, so they
    /// share one guard rather than growing a parallel path.
    /// </summary>
    private bool Spectating => AiTurn ||
        (_net is { IsOnline: true } && CurrentPlayer != _net.LocalSeat && !IsGameOver);
    private Snookering.Core.Ai.AiDifficulty AiDifficulty => (Snookering.Core.Ai.AiDifficulty)(_aiLevel - 1);

    public override void _Ready()
    {
        Ui.GameSettings.Load();

        _camera = new Camera3D { Name = "MatchCamera", Fov = 55f };
        AddChild(_camera);
        _camera.MakeCurrent();

        _cue = CueView.Create();
        AddChild(_cue);

        _audio = new AudioManager { Name = "Audio" };
        AddChild(_audio);

        _net = new Net.NetworkManager { Name = "Net" };
        AddChild(_net);

        BuildHud();

        // Honour the saved graphics preset on startup, not only when the
        // dropdown is touched.
        Ui.GameSettings.ApplyGraphics(GetViewport().World3D.Environment);

        // CLI test hooks: "--game snooker|8ball" selects the game,
        // "--break [power01]" fires a break immediately (harness verification).
        var args = OS.GetCmdlineUserArgs();
        var startType = GameType.EightBall;
        var netHost = false;
        string? netJoin = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--game" && i + 1 < args.Length && args[i + 1] == "snooker")
                startType = GameType.Snooker;
            if (args[i] == "--aimpitch" && i + 1 < args.Length)
                _aimPitch = Mathf.Clamp(Mathf.DegToRad(float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture)), MinAimPitch, MaxAimPitch);
            if (args[i] == "--aimdist" && i + 1 < args.Length)
                _aimDist = Mathf.Clamp(float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture), 0.25f, 1.6f);
            if (args[i] == "--host")
                netHost = true;
            if (args[i] == "--join" && i + 1 < args.Length)
                netJoin = args[i + 1];
            if (args[i] == "--ai" && i + 1 < args.Length)
                _aiLevel = Math.Clamp(int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture), 0, 3);
            if (args[i] == "--view" && i + 1 < args.Length && args[i + 1] == "top")
            {
                _forceTableView = true;
                _pitch = 1.35f;
                _dist = 2.3f;
            }
            if (args[i] == "--yaw" && i + 1 < args.Length)
            {
                _forceTableView = true;
                _yaw = Mathf.DegToRad(float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture));
            }
            if (args[i] == "--pitch" && i + 1 < args.Length)
            {
                _forceTableView = true;
                _pitch = Mathf.DegToRad(float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // Any CLI flag means we are being driven by the screenshot/state harness,
        // so drop straight into the match instead of opening on the menu.
        if (args.Length > 0)
            _menu.Show(Ui.MenuLayer.Screen.None);

        if (netHost)
        {
            GD.Print("[net] " + (_net.Host() is { Length: > 0 } e ? e : "hosting (CLI)"));
            _menu.Show(Ui.MenuLayer.Screen.None);
        }
        else if (netJoin is not null)
        {
            GD.Print("[net] " + (_net.Join(netJoin) is { Length: > 0 } e2 ? e2 : $"joining {netJoin} (CLI)"));
            _menu.Show(Ui.MenuLayer.Screen.None);
        }

        StartRack(startType);
        UpdateCamera(0f);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--break")
            {
                // Networked: wait for the opponent, otherwise the shot is fired
                // into an empty match and never replicated.
                if (netHost)
                {
                    var power = args.Length > i + 1 && float.TryParse(args[i + 1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var np)
                        ? Mathf.Clamp(np, 0f, 1f) : 1f;
                    _net.OpponentJoined += () =>
                    {
                        Rpc(MethodName.StartOnlineMatch, (int)startType, 0,
                            Net.NetworkManager.ProtocolVersion);
                        GetTree().CreateTimer(1.0).Timeout += () => { _power = power; Fire(); };
                    };
                    break;
                }

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
        // While a menu is up it owns the input, except for the key that closes it.
        if (_menu is not null && _menu.Blocking)
        {
            if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
                _menu.TogglePause();
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton mb:
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    if (Spectating)
                        break; // the AI or the remote player owns aiming and placement
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
                        _dist = Mathf.Clamp(_dist * 0.92f, 0.6f, 2.4f);
                }
                else if (mb.ButtonIndex == MouseButton.WheelDown)
                {
                    if (InAimView)
                        _aimDist = Mathf.Clamp(_aimDist * 1.08f, 0.25f, 1.6f);
                    else
                        _dist = Mathf.Clamp(_dist * 1.08f, 0.6f, 2.4f);
                }
                break;

            case InputEventMouseMotion mm:
                if (_aiming && InAimView)
                {
                    // The camera hangs behind the cue, so turning the aim IS turning the view.
                    _aimAngle -= mm.Relative.X * 0.002f * Ui.GameSettings.AimSensitivity * Mathf.Clamp(_aimDist / 0.6f, 0.4f, 1f);
                    _aimPitch = Mathf.Clamp(_aimPitch + mm.Relative.Y * 0.003f, MinAimPitch, MaxAimPitch);
                }
                else if (_orbiting)
                {
                    if (InAimView)
                    {
                        _aimAngle -= mm.Relative.X * 0.002f;
                        _aimPitch = Mathf.Clamp(_aimPitch + mm.Relative.Y * 0.003f, MinAimPitch, MaxAimPitch);
                    }
                    else
                    {
                        _yaw -= mm.Relative.X * 0.005f;
                        _pitch = Mathf.Clamp(_pitch + mm.Relative.Y * 0.005f, MinOrbitPitch, MaxOrbitPitch);
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
            case Key.Space when _mode == Mode.Aiming && !IsGameOver && !Spectating:
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
            case Key.Escape:
                _menu.TogglePause();
                break;
            case Key.H:
                _hud.ToggleHint();
                break;
            case Key.P:
                _aiLevel = (_aiLevel + 1) % 4;
                _aiTask = null;
                _aiPending = null;
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
        // Keep the dial in step when spin is set by key or cleared on a re-rack.
        if (_hud is not null && (_hud.Spin.SpinSide != side || _hud.Spin.SpinVert != vert))
            _hud.Spin.SetSpin(side, vert, notify: false);
    }

    /// <summary>(Re)build table, balls, and match state for the chosen game.</summary>
    private void StartRack(GameType type, int? breakingSeat = null)
    {
        var winnerBreaks = breakingSeat ?? (type == _gameType
            ? _gameType == GameType.EightBall
                ? (_game.GameOver && _game.Winner >= 0 ? _game.Winner : 0)
                : (_snookerGame.FrameOver && _snookerGame.Winner >= 0 ? _snookerGame.Winner : 0)
            : 0);

        _gameType = type;
        _table = type == GameType.EightBall ? TableSpec.Pool9ft() : TableSpec.Snooker12ft();
        _snookerRules = type == GameType.Snooker ? new SnookerRules(_table) : null;
        _game = new EightBallGame { CurrentPlayer = winnerBreaks };
        _snookerGame = new SnookerGame { CurrentPlayer = winnerBreaks };
        _message = $"{(type == GameType.EightBall ? "8-Ball" : "Snooker")} — Player {winnerBreaks + 1} to break";

        _tableNode?.QueueFree();
        _tableNode = new Node3D { Name = "TableRoot" };
        _tableNode.AddChild(TableBuilder.Build(_table));
        _tableNode.AddChild(EnvironmentBuilder.Build((float)_table.HalfLength, (float)_table.HalfWidth));
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
            // Snooker reds all share one texture; the maker's mark on every ball is
            // what makes applied spin visible on otherwise plain colours.
            var texture = type == GameType.EightBall
                ? $"res://assets/balls/pool_{id}.png"
                : $"res://assets/balls/snooker_{(SnookerBalls.IsRed(id) ? 1 : id)}.png";
            _views[i] = BallView.Create(id, (float)_table.Physics.R, color, texture);
            AddChild(_views[i]);
        }
        SnapViews();

        _audio.SetTable(_table);

        _result = null;
        _simTask = null;
        _mode = Mode.Aiming;
        _aimAngle = 0f;
        _snookerBreak = 0;
        SetSpin(0f, 0f);
        _hud?.ResetElevation();
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
            // Raising the cue makes the ball swerve; the core has always modelled
            // it, but every shot used to be sent perfectly level.
            ElevationCentiDeg = (short)Math.Round(_hud.ElevationDeg * 100f),
            CuePlaceXMicroM = _pendingPlacement is { } p1 ? (int)Math.Round(p1.X * 1e6) : null,
            CuePlaceYMicroM = _pendingPlacement is { } p2 ? (int)Math.Round(p2.Y * 1e6) : null,
        };

        if (_net.IsOnline)
        {
            // CallLocal, so both peers enter the same funnel with the same struct.
            Rpc(MethodName.SubmitShot,
                shot.AimAngleMicroRad, shot.SpeedMmPerSec,
                (int)shot.OffsetSide1e4, (int)shot.OffsetVert1e4, (int)shot.ElevationCentiDeg,
                shot.CuePlaceXMicroM ?? 0, shot.CuePlaceYMicroM ?? 0, shot.HasCuePlacement,
                _power);
            return;
        }

        FireInput(shot, _power);
    }

    /// <summary>Common firing path for human and AI shots.</summary>
    private void FireInput(in ShotInput shot, float visualPower)
    {
        _preShotState = _state;
        var state = _state;
        var table = _table;
        var input = shot;
        _simTask = System.Threading.Tasks.Task.Run(() => Simulator.Run(state, input, table));

        _shotIndex++;
        _firedPower = visualPower;
        _strikeT = 0f;
        _mode = Mode.Striking;
        _power = 0f;
    }

    // ------------------------------------------------------------------ AI turns

    private System.Collections.Generic.List<byte> LegalTargetsNow() =>
        _gameType == GameType.EightBall
            ? EightBallRules.LegalTargets(_game, _state)
            : SnookerRules.LegalTargets(_snookerGame, _state);

    private void UpdateAi(float dt)
    {
        if (!AiTurn)
        {
            _aiTask = null;
            _aiPending = null;
            return;
        }

        if (_mode == Mode.BallInHand)
        {
            _aiThink += dt;
            if (_aiThink < 0.9f)
                return;
            _placement = Snookering.Core.Ai.ShotPlanner.PlanPlacement(_state, _table, LegalTargetsNow(), _placementInD);
            _placementValid = true;
            ConfirmPlacement();
            _aiThink = 0f;
            return;
        }

        if (_mode != Mode.Aiming)
            return;

        if (_aiPending is null && _aiTask is null)
        {
            _aiThink = 0f;
            var state = _state;
            var table = _table;
            var targets = LegalTargetsNow();
            var difficulty = AiDifficulty;
            _aiSeed = _aiSeed * 6364136223846793005UL + 1442695040888963407UL;
            var seed = _aiSeed;
            var breakShot = _gameType == GameType.EightBall && !_game.BreakTaken;
            _aiTask = System.Threading.Tasks.Task.Run(() =>
                breakShot ? PlanBreak(state, table, seed) : Snookering.Core.Ai.ShotPlanner.Plan(state, table, targets, difficulty, seed));
        }

        _aiThink += dt;

        if (_aiTask is { IsCompleted: true })
        {
            if (_aiTask.IsCompletedSuccessfully)
                _aiPending = _aiTask.Result.Input;
            else
                GD.PrintErr($"[ai] planning failed: {_aiTask.Exception?.GetBaseException().Message}");
            _aiTask = null;
        }

        if (_aiPending is { } shot)
        {
            // Swing the cue visibly onto the AI's aim line, then fire.
            var target = (float)(shot.AimAngleMicroRad * 1e-6);
            _aimAngle = Mathf.LerpAngle(_aimAngle, target, 1f - Mathf.Exp(-5f * dt));
            if (_aiThink > 1.1f && Mathf.Abs(Mathf.AngleDifference(_aimAngle, target)) < 0.005f)
            {
                _aimAngle = target;
                var visual = Mathf.Clamp(((float)shot.Speed - 0.4f) / 6.6f, 0.05f, 1f);
                FireInput(shot, visual);
                _aiPending = null;
            }
        }
    }

    // ------------------------------------------------------------------ online

    /// <summary>
    /// Stream the aim while lining up, so the other player watches the cue move,
    /// the spin dial change and the power meter swing instead of staring at a
    /// still table until the shot suddenly happens. Sent unreliably a few times a
    /// second: a dropped frame here costs nothing, and the shot itself is what
    /// actually gets simulated.
    /// </summary>
    private void BroadcastAim(float dt)
    {
        if (!_net.IsOnline || Spectating || _mode != Mode.Aiming || !_net.OpponentPresent)
            return;

        _aimBroadcast += dt;
        if (_aimBroadcast < 0.06f)
            return;
        _aimBroadcast = 0f;

        Rpc(MethodName.AimUpdate, _aimAngle, _spinSide, _spinVert, _power,
            _mode == Mode.BallInHand ? 0f : (float)_hud.ElevationDeg);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void AimUpdate(float aim, float side, float vert, float power, float elevation)
    {
        if (!Spectating)
            return; // never let a stale packet fight the player for their own cue
        _aimAngle = aim;
        _power = power;
        SetSpin(side, vert);
        _spectatedElevation = elevation;
    }

    /// <summary>
    /// A shot arriving from either peer. Both sides run this — the sender via
    /// CallLocal — so the two simulations start from the same struct at the same
    /// point in the match.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SubmitShot(int aim, int speedMm, int side, int vert, int elevation,
                            int placeX, int placeY, bool hasPlacement, float visualPower)
    {
        if (_mode is not (Mode.Aiming or Mode.BallInHand))
        {
            GD.PrintErr("[net] shot arrived while busy — ignoring");
            return;
        }

        // Apply the placement to the table BEFORE the shot on both peers, so the
        // pre-shot state the rules engine sees is identical on each side.
        if (hasPlacement)
        {
            ref var cue = ref _state.Ball(0);
            cue = BallState.AtRest(0, new CoreVec2(placeX * 1e-6, placeY * 1e-6));
            SnapViews();
        }

        var shot = new ShotInput
        {
            AimAngleMicroRad = aim,
            SpeedMmPerSec = speedMm,
            OffsetSide1e4 = (short)side,
            OffsetVert1e4 = (short)vert,
            ElevationCentiDeg = (short)elevation,
            CuePlaceXMicroM = hasPlacement ? placeX : null,
            CuePlaceYMicroM = hasPlacement ? placeY : null,
        };

        _pendingPlacement = null;
        _aimAngle = (float)shot.AimAngleRad; // so the cue animation points the right way
        FireInput(shot, visualPower);
    }

    /// <summary>Host tells both peers to rack up. Racks are pure functions of the
    /// table spec, so naming the game and the breaker is enough.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void StartOnlineMatch(int gameType, int breakingSeat, int protocolVersion)
    {
        if (protocolVersion != Net.NetworkManager.ProtocolVersion)
        {
            _message = "Version mismatch — both players need the same build.";
            _net.Leave();
            _menu.Show(Ui.MenuLayer.Screen.Main);
            return;
        }

        _aiLevel = 0; // never run the AI in a networked match
        StartRack((GameType)gameType, breakingSeat);
        _menu.Show(Ui.MenuLayer.Screen.None);
    }

    /// <summary>
    /// Guest reports what it computed. The physics hash alone is not enough: the
    /// peers could agree on every ball and still disagree about whose turn it is.
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReportState(int shotIndex, long physicsHash, long rulesHash)
    {
        if (_net.Current != Net.NetworkManager.Role.Host)
            return;

        // The guest can finish a shot slightly before the host does. Comparing
        // against whatever the host happens to hold at that instant would flag a
        // desync on every shot, so wait until the host has the same shot.
        if (shotIndex != _shotIndex || _mode is Mode.Striking or Mode.Simulating or Mode.Playback)
        {
            _pendingReport = (shotIndex, (ulong)physicsHash, (ulong)rulesHash);
            return;
        }
        CompareWithGuest(shotIndex, (ulong)physicsHash, (ulong)rulesHash);
    }

    private void CompareWithGuest(int shotIndex, ulong physicsHash, ulong rulesHash)
    {
        if (shotIndex != _shotIndex)
            return;
        if (physicsHash == _lastPhysicsHash && rulesHash == _lastRulesHash)
        {
            GD.Print($"[net] shot {shotIndex} agreed");
            return;
        }

        GD.PrintErr($"[net] desync on shot {shotIndex} " +
                    $"(physics {physicsHash == _lastPhysicsHash}, rules {rulesHash == _lastRulesHash})" +
                    " — resyncing the guest from the host state");
        Rpc(MethodName.AdoptHostState, PackBalls(), CurrentPlayer, _snookerBreak);
    }

    /// <summary>Last-resort authority: the guest takes the host's ball positions.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AdoptHostState(double[] packed, int currentPlayer, int snookerBreak)
    {
        for (var i = 0; i < _state.Balls.Length && i * 3 + 2 < packed.Length; i++)
        {
            ref var b = ref _state.Balls[i];
            b.Pos = new CoreVec2(packed[i * 3], packed[i * 3 + 1]);
            b.Vel = CoreVec2.Zero;
            b.AngVel = new Snookering.Core.Mathematics.Vec3(0, 0, 0);
            b.State = MotionState.Stationary;
            b.OnTable = packed[i * 3 + 2] > 0.5;
        }
        if (_gameType == GameType.EightBall)
            _game.CurrentPlayer = currentPlayer;
        else
            _snookerGame.CurrentPlayer = currentPlayer;
        _snookerBreak = snookerBreak;
        _message = "Resynced with the host.";
        SnapViews();
    }

    private double[] PackBalls()
    {
        var packed = new double[_state.Balls.Length * 3];
        for (var i = 0; i < _state.Balls.Length; i++)
        {
            packed[i * 3] = _state.Balls[i].Pos.X;
            packed[i * 3 + 1] = _state.Balls[i].Pos.Y;
            packed[i * 3 + 2] = _state.Balls[i].OnTable ? 1.0 : 0.0;
        }
        return packed;
    }

    /// <summary>Full-power straight break at the rack apex.</summary>
    private static Snookering.Core.Ai.AiShot PlanBreak(TableState state, TableSpec table, ulong seed)
    {
        var cue = state.Ball(0).Pos;
        var apex = Racks.FootSpot(table);
        var dir = (apex - cue).Normalized();
        var angle = Math.Atan2(dir.Y, dir.X);
        return new Snookering.Core.Ai.AiShot(new ShotInput
        {
            AimAngleMicroRad = (int)Math.Round(angle * 1e6),
            SpeedMmPerSec = 6800,
            OffsetSide1e4 = 0,
            OffsetVert1e4 = 0,
            ElevationCentiDeg = 0,
            Seed = seed,
        }, "break");
    }

    private void FinishPlayback()
    {
        _audio.StopRolling();
        _lastPhysicsHash = _result!.StateHash;
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
            var shooter = _snookerGame.CurrentPlayer;
            var scoreBefore = _snookerGame.Scores[shooter];
            var outcome = _snookerRules!.Apply(_snookerGame, _preShotState!, _result);
            var gained = _snookerGame.Scores[shooter] - scoreBefore;
            _snookerBreak = outcome.TurnContinues && gained > 0 ? _snookerBreak + gained : 0;
            _message = outcome.Message;
            ballInHand = outcome.BallInHandInD;
            inD = ballInHand;
            over = outcome.FrameOver;
        }
        _result = null;

        _pendingPlacement = null;
        _lastRulesHash = _gameType == GameType.EightBall
            ? Snookering.Core.Rules.RulesHash.Of(_game)
            : Snookering.Core.Rules.RulesHash.Of(_snookerGame);

        if (_net.IsOnline)
            GD.Print($"[net] shot {_shotIndex} physics={_lastPhysicsHash:X16} rules={_lastRulesHash:X16}");

        // The guest reports; the host is the one that can rule on a mismatch.
        if (_net.Current == Net.NetworkManager.Role.Guest)
            RpcId(1, MethodName.ReportState, _shotIndex, (long)_lastPhysicsHash, (long)_lastRulesHash);
        else if (_pendingReport is { } queued)
        {
            _pendingReport = null;
            CompareWithGuest(queued.Shot, queued.Physics, queued.Rules);
        }

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
        // Quantize immediately: the position comes from a float camera raycast, and
        // both peers must rebuild the identical double from the same integers.
        var (qx, qy) = ShotInput.QuantizePlacement(_placement);
        _pendingPlacement = new CoreVec2(qx * 1e-6, qy * 1e-6);

        ref var cue = ref _state.Ball(0);
        cue = BallState.AtRest(0, _pendingPlacement.Value);
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

        UpdateAi((float)delta);
        BroadcastAim((float)delta);

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
                    _nextEventIdx = 0;
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
                PumpAudioEvents();
                if (_playTime >= _result.Duration + 0.3)
                    FinishPlayback();
                break;

            case Mode.BallInHand:
                if (!Spectating)
                    UpdatePlacement();
                break;
        }

        UpdateCamera((float)delta);
        UpdateCue();
        UpdateHud();
    }

    private readonly System.Collections.Generic.List<(Vector3 Pos, float Speed)> _movers = new();

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

            if (lerped.OnTable && interval > 0.0)
            {
                var speed = (float)((b.Pos - a.Pos).Length / interval);
                if (speed > 0.12f)
                    _movers.Add((_views[i].Position, speed));
            }
        }

        // Loudest few rolling balls get a voice; the rest are inaudible anyway.
        _movers.Sort((x, y) => y.Speed.CompareTo(x.Speed));
        _audio.UpdateRolling(_movers, dt);
        _movers.Clear();
    }

    /// <summary>Fire the sound of every sim event whose time playback has just passed.</summary>
    private void PumpAudioEvents()
    {
        var events = _result!.Events;
        while (_nextEventIdx < events.Count && events[_nextEventIdx].Time <= _playTime)
        {
            var e = events[_nextEventIdx++];
            if (e.Type is SimEventType.RestReached)
                continue;

            if (e.Type is SimEventType.Pocketed && e.BallA != 0)
                foreach (var npc in GetTree().GetNodesInGroup("npcs"))
                    (npc as NpcView)?.React();

            var pos = Vector3.Zero;
            foreach (var v in _views)
            {
                if (v.BallId == e.BallA)
                {
                    pos = v.Position;
                    break;
                }
            }
            _audio.PlayEvent(in e, pos);
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

    /// <summary>CLI: --view top forces the table orbit view (debug screenshots).</summary>
    private bool _forceTableView;

    private bool InAimView => !_forceTableView && _mode is Mode.Aiming or Mode.Striking;

    private void UpdateCamera(float dt)
    {
        Vector3 targetPos, targetLook;

        // Menus drift slowly around the table so the backdrop is alive.
        if (_menu is not null && _menu.Blocking)
        {
            _menuYaw += dt * 0.06f;
            var menuOffset = new Vector3(
                Mathf.Sin(_menuYaw) * Mathf.Cos(0.42f),
                Mathf.Sin(0.42f),
                Mathf.Cos(_menuYaw) * Mathf.Cos(0.42f)) * 2.5f;
            targetPos = TableFocus + menuOffset;
            targetPos.Y = Mathf.Clamp(targetPos.Y, MinOrbitHeight, MaxEyeHeight);
            targetLook = TableFocus;
        }
        else if (InAimView)
        {
            // Low behind the cue ball, looking down the aim line.
            var ball = CueBallPosition();
            var dir = new Vector3(Mathf.Cos(_aimAngle), 0f, -Mathf.Sin(_aimAngle));
            targetPos = ball
                        - dir * (_aimDist * Mathf.Cos(_aimPitch))
                        + Vector3.Up * (_aimDist * Mathf.Sin(_aimPitch));
            targetPos.Y = Mathf.Clamp(targetPos.Y, MinEyeHeight, MaxEyeHeight);
            targetLook = ball + dir * 0.6f;
        }
        else
        {
            var offset = new Vector3(
                Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
                Mathf.Sin(_pitch),
                Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _dist;
            targetPos = TableFocus + offset;
            targetPos.Y = Mathf.Clamp(targetPos.Y, MinOrbitHeight, MaxEyeHeight);
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
        AudioBuses.SetListenerDistance(_camPos.DistanceTo(TableFocus));

        // Steep top-down table view: cull the lamp shades so they don't block the
        // view (their lights keep shining — only the meshes vanish).
        var hideLamps = !InAimView && _pitch > 1.15f;
        _camera.CullMask = hideLamps ? ~EnvironmentBuilder.LampLayer : uint.MaxValue;
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

    // ------------------------------------------------------------------ HUD & menus

    private void BuildHud()
    {
        _hud = new Ui.Hud { Name = "Hud" };
        AddChild(_hud);
        _hud.Spin.Changed += () => SetSpin(_hud.Spin.SpinSide, _hud.Spin.SpinVert);

        _menu = new Ui.MenuLayer { Name = "Menu" };
        AddChild(_menu);
        _menu.StartRequested += (snooker, ai) =>
        {
            _aiLevel = ai;
            StartRack(snooker ? GameType.Snooker : GameType.EightBall);
        };
        _menu.RestartRequested += () => StartRack(_gameType);
        _menu.SwitchGameRequested += () =>
            StartRack(_gameType == GameType.EightBall ? GameType.Snooker : GameType.EightBall);
        _menu.MainMenuRequested += () => { };

        _menu.HostRequested += () =>
        {
            var error = _net.Host();
            _menu.SetOnlineStatus(error.Length > 0
                ? error
                : $"Hosting on UDP port {Net.NetworkManager.DefaultPort}.  Same network? Your friend joins with:  " +
                  string.Join("  or  ", Net.NetworkManager.LocalAddresses()) +
                  "\nDifferent network? Point a tunnel at that port and send them the public address it gives you — see MULTIPLAYER.md.", false);
        };

        _menu.JoinRequested += address =>
        {
            var error = _net.Join(address);
            _menu.SetOnlineStatus(error.Length > 0 ? error : $"Connecting to {address}…", false);
        };

        _menu.OnlineStartRequested += snooker =>
        {
            if (_net.Current != Net.NetworkManager.Role.Host)
                return;
            Rpc(MethodName.StartOnlineMatch, (int)(snooker ? GameType.Snooker : GameType.EightBall),
                0, Net.NetworkManager.ProtocolVersion);
        };

        _menu.LeaveMatchRequested += () => _net.Leave();

        _net.OpponentJoined += () => _menu.SetOnlineStatus(
            _net.Current == Net.NetworkManager.Role.Host
                ? "Opponent connected. Pick a game and start."
                : "Connected. Waiting for the host to start…",
            _net.Current == Net.NetworkManager.Role.Host);

        _net.Disconnected += reason =>
        {
            _message = reason;
            _menu.SetOnlineStatus(reason, false);
            _menu.Show(Ui.MenuLayer.Screen.Main);
        };
        _net.Failed += reason => _menu.SetOnlineStatus(reason, false);
    }

    private void UpdateHud()
    {
        var state = new Ui.HudState
        {
            Snooker = _gameType == GameType.Snooker,
            CurrentPlayer = CurrentPlayer,
            GameOver = IsGameOver,
            AiTurn = Spectating,
            AiLabel = _aiLevel switch
            {
                1 => "AI — easy",
                2 => "AI — medium",
                3 => "AI — hard",
                _ => "",
            },
            Power = _power,
            Charging = _charging,
            Online = _net.IsOnline,
            LocalSeat = _net.IsOnline ? _net.LocalSeat : -1,
            ShowAimControls = _mode == Mode.Aiming && !Spectating && !IsGameOver,
            Message = Spectating && _mode is Mode.Aiming or Mode.BallInHand
                ? (_net.IsOnline ? "Opponent is aiming…" : "Opponent is thinking…")
                : _mode == Mode.BallInHand
                    ? "Ball in hand — click to place the cue ball"
                    : _message,
        };

        if (_gameType == GameType.EightBall)
        {
            string GroupName(int p) => _game.GroupOf(p) switch
            {
                BallGroup.Solids => "solids",
                BallGroup.Stripes => "stripes",
                _ => "",
            };
            state.OpenTable = _game.OpenTable;
            state.GroupP1 = GroupName(0);
            state.GroupP2 = GroupName(1);
            state.RemainingP1 = RemainingIn(_game.GroupOf(0));
            state.RemainingP2 = RemainingIn(_game.GroupOf(1));
        }
        else
        {
            var g = _snookerGame;
            state.ScoreP1 = g.Scores[0];
            state.ScoreP2 = g.Scores[1];
            state.Break = _snookerBreak;
            state.BallOn = g.ColorsPhase
                ? SnookerRules.ColorName(g.NextColorOn)
                : g.ColorBallOn ? "a colour" : "a red";
            state.BallOnColor = g.ColorsPhase
                ? BallView.SnookerColor(g.NextColorOn)
                : g.ColorBallOn ? new Color(0.85f, 0.85f, 0.85f) : BallView.SnookerColor(1);
        }

        _hud.Visible = !_menu.Blocking;
        _hud.Update(in state);
    }

    /// <summary>How many of a player's own balls are still on the table.</summary>
    private int RemainingIn(BallGroup group)
    {
        if (group == BallGroup.None)
            return 0;
        var count = 0;
        foreach (var b in _state.Balls)
        {
            if (!b.OnTable || b.Id == 0 || b.Id == 8)
                continue;
            var solid = b.Id is >= 1 and <= 7;
            if ((group == BallGroup.Solids) == solid)
                count++;
        }
        return count;
    }
}
