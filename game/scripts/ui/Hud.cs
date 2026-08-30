using Godot;

namespace Snookering.Game.Ui;

/// <summary>What the HUD needs to know each frame. Filled by MatchController.</summary>
public struct HudState
{
    public bool Snooker;
    public int CurrentPlayer;
    public bool GameOver;
    public bool AiTurn;
    public string AiLabel;        // "" when player 2 is human
    /// <summary>-1 offline/hotseat, else which seat this machine plays.</summary>
    public int LocalSeat;
    public bool Online;
    public string Message;
    public float Power;
    public bool Charging;
    public bool ShowAimControls;

    // Pool
    public string GroupP1, GroupP2;   // "solids" / "stripes" / "" when the table is open
    public int RemainingP1, RemainingP2;
    public bool OpenTable;

    // Snooker
    public int ScoreP1, ScoreP2, Break;
    public string BallOn;
    public Color BallOnColor;
}

/// <summary>
/// The in-match interface: a card per player that adapts to the game being played,
/// a power meter, the spin selector, and a message line for fouls and turn changes.
/// </summary>
public partial class Hud : CanvasLayer
{
    public SpinWidget Spin { get; private set; } = null!;

    /// <summary>Cue elevation in degrees (0 = level). Raising the butt makes the
    /// cue ball swerve, which the physics core has always modelled.</summary>
    public float ElevationDeg => (float)_elevation.Value;

    private HSlider _elevation = null!;
    private Label _elevationLabel = null!;

    private PanelContainer _cardP1 = null!, _cardP2 = null!;
    private Label _nameP1 = null!, _nameP2 = null!;
    private Label _detailP1 = null!, _detailP2 = null!;
    private Label _statP1 = null!, _statP2 = null!;
    private BallChip _chipP1 = null!, _chipP2 = null!;
    private Label _title = null!, _message = null!, _hint = null!;
    private ColorRect _powerFill = null!;
    private Control _powerMarker = null!;
    private Control _aimPanel = null!;
    private PanelContainer _hintFrame = null!;
    private bool _hintVisible = true;

    public override void _Ready()
    {
        Layer = 1;
        BuildPlayerCards();
        BuildTitle();
        BuildMessage();
        BuildPowerMeter();
        BuildAimPanel();
        BuildHint();
    }

    public void ToggleHint()
    {
        _hintVisible = !_hintVisible;
        _hintFrame.Visible = _hintVisible;
    }

    // ------------------------------------------------------------------ build

    private void BuildPlayerCards()
    {
        (_cardP1, _nameP1, _detailP1, _statP1, _chipP1) = MakeCard(left: true);
        (_cardP2, _nameP2, _detailP2, _statP2, _chipP2) = MakeCard(left: false);
    }

    private (PanelContainer, Label, Label, Label, BallChip) MakeCard(bool left)
    {
        var card = UiTheme.MakePanel();
        card.SetAnchorsPreset(left ? Control.LayoutPreset.TopLeft : Control.LayoutPreset.TopRight);
        card.OffsetLeft = left ? 18 : -258;
        card.OffsetRight = left ? 258 : -18;
        card.OffsetTop = 16;
        card.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(card);

        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        card.AddChild(row);

        var chip = new BallChip { CustomMinimumSize = new Vector2(30, 30) };
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 0);
        var name = UiTheme.MakeLabel("Player", 17, UiTheme.Text);
        var detail = UiTheme.MakeLabel("", 13, UiTheme.TextDim);
        text.AddChild(name);
        text.AddChild(detail);
        var stat = UiTheme.MakeLabel("", 26, UiTheme.Text, HorizontalAlignment.Right);

        if (left)
        {
            row.AddChild(chip);
            row.AddChild(text);
            row.AddChild(stat);
        }
        else
        {
            row.AddChild(stat);
            row.AddChild(text);
            row.AddChild(chip);
        }
        return (card, name, detail, stat, chip);
    }

    private void BuildTitle()
    {
        _title = UiTheme.MakeLabel("", 15, UiTheme.TextDim, HorizontalAlignment.Center);
        _title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _title.OffsetTop = 22;
        AddChild(_title);
    }

    private void BuildMessage()
    {
        _message = UiTheme.MakeLabel("", 22, UiTheme.Accent, HorizontalAlignment.Center);
        _message.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _message.AnchorLeft = 0f;
        _message.AnchorRight = 1f;
        _message.OffsetTop = 96;
        AddChild(_message);
    }

    private void BuildPowerMeter()
    {
        var frame = UiTheme.MakePanel(new Color(0.03f, 0.03f, 0.04f, 0.75f), UiTheme.AccentDim);
        frame.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        frame.AnchorLeft = 0.5f;
        frame.AnchorRight = 0.5f;
        frame.OffsetLeft = -190;
        frame.OffsetRight = 190;
        frame.OffsetTop = -62;
        frame.OffsetBottom = -22;
        frame.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(frame);

        var track = new Control { CustomMinimumSize = new Vector2(0, 22), MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(track);

        _powerFill = new ColorRect
        {
            Color = UiTheme.Accent,
            AnchorBottom = 1f,
            AnchorRight = 0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        track.AddChild(_powerFill);

        _powerMarker = new ColorRect
        {
            Color = UiTheme.Text,
            AnchorBottom = 1f,
            CustomMinimumSize = new Vector2(2, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        track.AddChild(_powerMarker);

        var caption = UiTheme.MakeLabel("POWER", 11, UiTheme.TextDim, HorizontalAlignment.Center);
        caption.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        caption.AnchorLeft = 0f;
        caption.AnchorRight = 1f;
        caption.OffsetTop = -20;
        AddChild(caption);
    }

    private void BuildAimPanel()
    {
        var panel = UiTheme.MakePanel();
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        panel.OffsetLeft = -168;
        panel.OffsetRight = -18;
        panel.OffsetTop = -258;
        panel.OffsetBottom = -18;
        AddChild(panel);
        _aimPanel = panel;

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        panel.AddChild(col);

        col.AddChild(UiTheme.MakeLabel("SPIN", 11, UiTheme.TextDim, HorizontalAlignment.Center));
        Spin = new SpinWidget();
        col.AddChild(Spin);

        _elevationLabel = UiTheme.MakeLabel("CUE LEVEL", 11, UiTheme.TextDim, HorizontalAlignment.Center);
        col.AddChild(_elevationLabel);
        _elevation = new HSlider
        {
            MinValue = 0,
            MaxValue = 15,
            Step = 1,
            Value = 0,
            CustomMinimumSize = new Vector2(0, 22),
            TooltipText = "Raise the butt of the cue to swerve around a blocking ball",
        };
        _elevation.ValueChanged += _ => UpdateElevationCaption();
        col.AddChild(_elevation);
    }

    private void UpdateElevationCaption()
    {
        var deg = (int)_elevation.Value;
        _elevationLabel.Text = deg == 0 ? "CUE LEVEL" : $"CUE RAISED {deg}°";
        _elevationLabel.AddThemeColorOverride("font_color", deg == 0 ? UiTheme.TextDim : UiTheme.Accent);
    }

    public void ResetElevation()
    {
        _elevation.Value = 0;
        UpdateElevationCaption();
    }

    private void BuildHint()
    {
        // On a bright cloth the bare text was unreadable, so it gets its own panel.
        var frame = UiTheme.MakePanel(new Color(0.03f, 0.03f, 0.04f, 0.55f));
        frame.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        frame.OffsetLeft = 14;
        frame.OffsetTop = -92;
        frame.OffsetBottom = -14;
        frame.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(frame);

        _hint = UiTheme.MakeLabel("", 12, UiTheme.Text);
        frame.AddChild(_hint);
        _hintFrame = frame;
        _hint.Text =
            "[LMB drag] aim     [RMB drag] look     [wheel] zoom\n" +
            "[Space] hold & release to shoot     [drag the ball] spin\n" +
            "[R] re-rack   [G] switch game   [P] opponent   [H] hide help   [Esc] menu";
    }

    // ------------------------------------------------------------------ update

    public void Update(in HudState s)
    {
        _title.Text = s.Snooker ? "SNOOKER" : "8-BALL";

        var p2Name = string.IsNullOrEmpty(s.AiLabel) ? "Player 2" : s.AiLabel;
        // Online, "Player 2" is meaningless on the guest's screen — say who is who.
        _nameP1.Text = s.Online ? (s.LocalSeat == 0 ? "You" : "Opponent") : "Player 1";
        _nameP2.Text = s.Online ? (s.LocalSeat == 1 ? "You" : "Opponent") : p2Name;

        if (s.Snooker)
        {
            _statP1.Text = s.ScoreP1.ToString();
            _statP2.Text = s.ScoreP2.ToString();
            _detailP1.Text = s.CurrentPlayer == 0 && s.Break > 0 ? $"break {s.Break}" : "";
            _detailP2.Text = s.CurrentPlayer == 1 && s.Break > 0 ? $"break {s.Break}" : "";
            _chipP1.Set(s.CurrentPlayer == 0 ? s.BallOnColor : new Color(0.2f, 0.2f, 0.2f, 0.5f), false);
            _chipP2.Set(s.CurrentPlayer == 1 ? s.BallOnColor : new Color(0.2f, 0.2f, 0.2f, 0.5f), false);
            _title.Text = s.GameOver ? "SNOOKER — FRAME OVER" : $"SNOOKER — on {s.BallOn}";
        }
        else
        {
            _statP1.Text = s.OpenTable ? "" : s.RemainingP1.ToString();
            _statP2.Text = s.OpenTable ? "" : s.RemainingP2.ToString();
            _detailP1.Text = s.OpenTable ? "open table" : s.GroupP1;
            _detailP2.Text = s.OpenTable ? "open table" : s.GroupP2;
            _chipP1.Set(GroupColor(s.GroupP1), s.GroupP1 == "stripes");
            _chipP2.Set(GroupColor(s.GroupP2), s.GroupP2 == "stripes");
            _title.Text = s.GameOver ? "8-BALL — GAME OVER" : "8-BALL";
        }

        Highlight(_cardP1, s.CurrentPlayer == 0 && !s.GameOver);
        Highlight(_cardP2, s.CurrentPlayer == 1 && !s.GameOver);

        _message.Text = s.Message;
        _message.AddThemeColorOverride("font_color",
            s.GameOver ? UiTheme.Good : s.Message.StartsWith("FOUL") ? UiTheme.Danger : UiTheme.Accent);

        _powerFill.AnchorRight = s.Power;
        _powerMarker.Visible = s.Charging;
        _powerMarker.AnchorLeft = s.Power;
        _powerMarker.AnchorRight = s.Power;

        _aimPanel.Visible = s.ShowAimControls;
    }

    private static Color GroupColor(string group) => group switch
    {
        "solids" => new Color(0.95f, 0.77f, 0.06f),
        "stripes" => new Color(0.85f, 0.25f, 0.20f),
        _ => new Color(0.35f, 0.35f, 0.35f, 0.6f),
    };

    private static void Highlight(PanelContainer card, bool active) =>
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(
            active ? UiTheme.PanelActive : UiTheme.Panel,
            active ? UiTheme.Accent : new Color(0, 0, 0, 0)));

    /// <summary>A small drawn ball used as the group / ball-on indicator.</summary>
    private partial class BallChip : Control
    {
        private Color _color = Colors.Gray;
        private bool _striped;

        public void Set(Color color, bool striped)
        {
            _color = color;
            _striped = striped;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var c = Size * 0.5f;
            var r = Mathf.Min(Size.X, Size.Y) * 0.5f - 2f;
            if (_striped)
            {
                DrawCircle(c, r, new Color(0.92f, 0.90f, 0.86f));
                // Latitude band, the way a striped ball actually reads.
                DrawRect(new Rect2(c.X - r, c.Y - r * 0.45f, r * 2f, r * 0.9f), _color);
                DrawCircle(c, r, new Color(0, 0, 0, 0)); // keep the circle bounds crisp
                DrawArc(c, r, 0f, Mathf.Tau, 32, new Color(0.2f, 0.2f, 0.2f, 0.8f), 1.5f, true);
            }
            else
            {
                DrawCircle(c, r, _color);
                DrawArc(c, r, 0f, Mathf.Tau, 32, new Color(0.2f, 0.2f, 0.2f, 0.8f), 1.5f, true);
            }
            DrawCircle(c + new Vector2(-r * 0.3f, -r * 0.3f), r * 0.3f, new Color(1f, 1f, 1f, 0.25f));
        }
    }
}
