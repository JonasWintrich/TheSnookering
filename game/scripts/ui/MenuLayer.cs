using System;
using Godot;

namespace Snookering.Game.Ui;

/// <summary>
/// Main menu, pause menu and settings, drawn over the live lounge — the table
/// keeps rendering behind the panels, so there are no scene loads and the menu
/// gets its background for free.
/// </summary>
public partial class MenuLayer : CanvasLayer
{
    public enum Screen { None, Main, Pause, Settings, Online }

    /// <summary>(snooker, aiLevel) — start a fresh match.</summary>
    public event Action<bool, int>? StartRequested;
    public event Action? ResumeRequested;
    public event Action? RestartRequested;
    public event Action? SwitchGameRequested;
    public event Action? MainMenuRequested;
    public event Action? HostRequested;
    public event Action<string>? JoinRequested;
    /// <summary>(snooker) — host starts the online match.</summary>
    public event Action<bool>? OnlineStartRequested;
    public event Action? LeaveMatchRequested;

    public Screen Current { get; private set; } = Screen.Main;
    public bool Blocking => Current != Screen.None;

    private ColorRect _dim = null!;
    private Control _main = null!, _pause = null!, _settings = null!, _online = null!;
    private Label _onlineStatus = null!;
    private LineEdit _address = null!;
    private OptionButton _onlineGame = null!;
    private Button _startOnline = null!;
    private OptionButton _opponent = null!;
    private Screen _settingsReturn = Screen.Main;

    public override void _Ready()
    {
        Layer = 10;

        _dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.72f) };
        _dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _dim.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_dim);

        _main = BuildMain();
        _pause = BuildPause();
        _settings = BuildSettings();
        _online = BuildOnline();

        Show(Screen.Main);
    }

    public void Show(Screen screen)
    {
        Current = screen;
        _dim.Visible = screen != Screen.None;
        _main.Visible = screen == Screen.Main;
        _pause.Visible = screen == Screen.Pause;
        _settings.Visible = screen == Screen.Settings;
        _online.Visible = screen == Screen.Online;

        if (screen == Screen.None)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GameSettings.Save();
        }
    }

    public void TogglePause()
    {
        if (Current == Screen.None)
            Show(Screen.Pause);
        else if (Current == Screen.Pause)
            Resume();
        else if (Current == Screen.Settings)
            Show(_settingsReturn);
    }

    private void Resume()
    {
        Show(Screen.None);
        ResumeRequested?.Invoke();
    }

    // ------------------------------------------------------------------ screens

    private VBoxContainer Centre(int width = 340)
    {
        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.Center);
        box.OffsetLeft = -width / 2;
        box.OffsetRight = width / 2;
        box.OffsetTop = -230;
        box.OffsetBottom = 230;
        box.AddThemeConstantOverride("separation", 10);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(box);
        return box;
    }

    private Control BuildMain()
    {
        var box = Centre();

        var title = UiTheme.MakeLabel("SNOOKERING", 46, UiTheme.Accent, HorizontalAlignment.Center);
        box.AddChild(title);
        box.AddChild(UiTheme.MakeLabel("pool & snooker in a quiet lounge", 14, UiTheme.TextDim, HorizontalAlignment.Center));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

        var opponentRow = new HBoxContainer();
        opponentRow.AddThemeConstantOverride("separation", 10);
        opponentRow.Alignment = BoxContainer.AlignmentMode.Center;
        var opponentLabel = UiTheme.MakeLabel("Opponent", 15, UiTheme.TextDim);
        opponentLabel.CustomMinimumSize = new Vector2(90, 0);
        opponentRow.AddChild(opponentLabel);
        _opponent = new OptionButton { CustomMinimumSize = new Vector2(180, 38) };
        _opponent.AddItem("Human (hotseat)", 0);
        _opponent.AddItem("AI — easy", 1);
        _opponent.AddItem("AI — medium", 2);
        _opponent.AddItem("AI — hard", 3);
        _opponent.Selected = 2;
        opponentRow.AddChild(_opponent);
        box.AddChild(opponentRow);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var pool = UiTheme.MakeButton("Play 8-Ball");
        pool.Pressed += () => { Show(Screen.None); StartRequested?.Invoke(false, _opponent.Selected); };
        box.AddChild(pool);

        var snooker = UiTheme.MakeButton("Play Snooker");
        snooker.Pressed += () => { Show(Screen.None); StartRequested?.Invoke(true, _opponent.Selected); };
        box.AddChild(snooker);

        var online = UiTheme.MakeButton("Play Online");
        online.Pressed += () => Show(Screen.Online);
        box.AddChild(online);

        var settings = UiTheme.MakeButton("Settings");
        settings.Pressed += () => { _settingsReturn = Screen.Main; Show(Screen.Settings); };
        box.AddChild(settings);

        var quit = UiTheme.MakeButton("Quit");
        quit.Pressed += () => GetTree().Quit();
        box.AddChild(quit);

        return box;
    }

    private Control BuildPause()
    {
        var box = Centre();
        box.AddChild(UiTheme.MakeLabel("PAUSED", 38, UiTheme.Accent, HorizontalAlignment.Center));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });

        var resume = UiTheme.MakeButton("Resume");
        resume.Pressed += Resume;
        box.AddChild(resume);

        var restart = UiTheme.MakeButton("Restart rack");
        restart.Pressed += () => { Show(Screen.None); RestartRequested?.Invoke(); };
        box.AddChild(restart);

        var switchGame = UiTheme.MakeButton("Switch game");
        switchGame.Pressed += () => { Show(Screen.None); SwitchGameRequested?.Invoke(); };
        box.AddChild(switchGame);

        var settings = UiTheme.MakeButton("Settings");
        settings.Pressed += () => { _settingsReturn = Screen.Pause; Show(Screen.Settings); };
        box.AddChild(settings);

        var main = UiTheme.MakeButton("Main menu");
        main.Pressed += () => { LeaveMatchRequested?.Invoke(); Show(Screen.Main); MainMenuRequested?.Invoke(); };
        box.AddChild(main);

        return box;
    }

    private Control BuildOnline()
    {
        var box = Centre(460);
        box.AddChild(UiTheme.MakeLabel("PLAY ONLINE", 34, UiTheme.Accent, HorizontalAlignment.Center));

        _onlineStatus = UiTheme.MakeLabel(
            "One player hosts, the other joins with their address.",
            14, UiTheme.TextDim, HorizontalAlignment.Center);
        _onlineStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _onlineStatus.CustomMinimumSize = new Vector2(440, 44);
        box.AddChild(_onlineStatus);

        var host = UiTheme.MakeButton("Host a match");
        host.Pressed += () => HostRequested?.Invoke();
        box.AddChild(host);

        var joinRow = new HBoxContainer();
        joinRow.AddThemeConstantOverride("separation", 8);
        _address = new LineEdit
        {
            PlaceholderText = "host address  (1.2.3.4  or  1.2.3.4:56789)",
            CustomMinimumSize = new Vector2(280, 44),
        };
        joinRow.AddChild(_address);
        var join = UiTheme.MakeButton("Join");
        join.CustomMinimumSize = new Vector2(150, 44);
        join.Pressed += () => JoinRequested?.Invoke(_address.Text);
        joinRow.AddChild(join);
        box.AddChild(joinRow);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        var gameRow = new HBoxContainer();
        gameRow.AddThemeConstantOverride("separation", 10);
        gameRow.Alignment = BoxContainer.AlignmentMode.Center;
        var gameLabel = UiTheme.MakeLabel("Game", 15, UiTheme.TextDim);
        gameLabel.CustomMinimumSize = new Vector2(70, 0);
        gameRow.AddChild(gameLabel);
        _onlineGame = new OptionButton { CustomMinimumSize = new Vector2(200, 38) };
        _onlineGame.AddItem("8-Ball", 0);
        _onlineGame.AddItem("Snooker", 1);
        gameRow.AddChild(_onlineGame);
        box.AddChild(gameRow);

        _startOnline = UiTheme.MakeButton("Start match");
        _startOnline.Disabled = true;
        _startOnline.Pressed += () => OnlineStartRequested?.Invoke(_onlineGame.Selected == 1);
        box.AddChild(_startOnline);

        var back = UiTheme.MakeButton("Back");
        back.Pressed += () => { LeaveMatchRequested?.Invoke(); Show(Screen.Main); };
        box.AddChild(back);

        return box;
    }

    /// <summary>Called by the match as the connection state changes.</summary>
    public void SetOnlineStatus(string text, bool hostCanStart)
    {
        _onlineStatus.Text = text;
        _startOnline.Disabled = !hostCanStart;
        _onlineGame.Disabled = !hostCanStart;
    }

    private Control BuildSettings()
    {
        var box = Centre(420);
        box.AddChild(UiTheme.MakeLabel("SETTINGS", 34, UiTheme.Accent, HorizontalAlignment.Center));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        HSlider Slider(string caption, float value, Action<float> onChange)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var label = UiTheme.MakeLabel(caption, 15, UiTheme.Text);
            label.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(label);
            var slider = new HSlider
            {
                MinValue = 0, MaxValue = 1, Step = 0.01, Value = value,
                CustomMinimumSize = new Vector2(220, 26),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            slider.ValueChanged += v => onChange((float)v);
            row.AddChild(slider);
            box.AddChild(row);
            return slider;
        }

        Slider("Master volume", GameSettings.MasterVolume, v => { GameSettings.MasterVolume = v; GameSettings.ApplyAudio(); });
        Slider("Effects volume", GameSettings.SfxVolume, v => { GameSettings.SfxVolume = v; GameSettings.ApplyAudio(); });
        Slider("Room ambience", GameSettings.AmbienceVolume, v => { GameSettings.AmbienceVolume = v; GameSettings.ApplyAudio(); });

        var sens = new HBoxContainer();
        sens.AddThemeConstantOverride("separation", 12);
        var sensLabel = UiTheme.MakeLabel("Aim sensitivity", 15, UiTheme.Text);
        sensLabel.CustomMinimumSize = new Vector2(150, 0);
        sens.AddChild(sensLabel);
        var sensSlider = new HSlider
        {
            MinValue = 0.3, MaxValue = 2.5, Step = 0.05, Value = GameSettings.AimSensitivity,
            CustomMinimumSize = new Vector2(220, 26),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        sensSlider.ValueChanged += v => GameSettings.AimSensitivity = (float)v;
        sens.AddChild(sensSlider);
        box.AddChild(sens);

        var gfx = new HBoxContainer();
        gfx.AddThemeConstantOverride("separation", 12);
        var gfxLabel = UiTheme.MakeLabel("Graphics", 15, UiTheme.Text);
        gfxLabel.CustomMinimumSize = new Vector2(150, 0);
        gfx.AddChild(gfxLabel);
        var preset = new OptionButton { CustomMinimumSize = new Vector2(220, 34) };
        preset.AddItem("Low", 0);
        preset.AddItem("Medium", 1);
        preset.AddItem("High", 2);
        preset.Selected = GameSettings.GraphicsPreset;
        preset.ItemSelected += id =>
        {
            GameSettings.GraphicsPreset = (int)id;
            GameSettings.ApplyGraphics(GetViewport().World3D.Environment);
        };
        gfx.AddChild(preset);
        box.AddChild(gfx);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        var back = UiTheme.MakeButton("Back");
        back.Pressed += () => Show(_settingsReturn);
        box.AddChild(back);

        return box;
    }
}
