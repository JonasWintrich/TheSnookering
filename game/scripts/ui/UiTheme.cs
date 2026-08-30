using Godot;

namespace Snookering.Game.Ui;

/// <summary>
/// One place for the interface look: dark glass panels with a warm accent that
/// matches the pendant lamps, so the UI belongs to the same room as the table.
/// </summary>
public static class UiTheme
{
    public static readonly Color Accent = new(0.96f, 0.74f, 0.38f);
    public static readonly Color AccentDim = new(0.42f, 0.33f, 0.20f);
    public static readonly Color Text = new(0.93f, 0.91f, 0.87f);
    public static readonly Color TextDim = new(0.60f, 0.58f, 0.54f);
    public static readonly Color Panel = new(0.045f, 0.045f, 0.055f, 0.80f);
    public static readonly Color PanelActive = new(0.13f, 0.10f, 0.06f, 0.92f);
    public static readonly Color Danger = new(0.93f, 0.42f, 0.30f);
    public static readonly Color Good = new(0.55f, 0.85f, 0.45f);

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 8, int borderWidth = 2)
    {
        var box = new StyleBoxFlat
        {
            BgColor = bg,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        };
        if (border is { } b)
        {
            box.BorderColor = b;
            box.BorderWidthLeft = borderWidth;
            box.BorderWidthRight = borderWidth;
            box.BorderWidthTop = borderWidth;
            box.BorderWidthBottom = borderWidth;
        }
        return box;
    }

    public static Label MakeLabel(string text, int size, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    /// <summary>A menu button styled to match the panels.</summary>
    public static Button MakeButton(string text, int size = 20)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(280, 46) };
        button.AddThemeFontSizeOverride("font_size", size);
        button.AddThemeColorOverride("font_color", Text);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeColorOverride("font_pressed_color", Accent);
        button.AddThemeColorOverride("font_focus_color", Accent);
        button.AddThemeStyleboxOverride("normal", Box(new Color(0.10f, 0.10f, 0.11f, 0.88f), AccentDim));
        button.AddThemeStyleboxOverride("hover", Box(new Color(0.17f, 0.14f, 0.09f, 0.94f), Accent));
        button.AddThemeStyleboxOverride("pressed", Box(new Color(0.24f, 0.19f, 0.11f, 0.96f), Accent));
        button.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), Accent));
        button.AddThemeStyleboxOverride("disabled", Box(new Color(0.08f, 0.08f, 0.08f, 0.7f)));
        return button;
    }

    public static PanelContainer MakePanel(Color? bg = null, Color? border = null)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Box(bg ?? Panel, border));
        return panel;
    }
}
