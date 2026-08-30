using System;
using Godot;

namespace Snookering.Game.Ui;

/// <summary>
/// The cue-ball spin selector: drag the contact point anywhere on the ball to set
/// follow, draw and english, exactly as you would address a real cue ball. The
/// dashed ring is the miscue limit — beyond it a real tip slides off the ball.
/// </summary>
public partial class SpinWidget : Control
{
    /// <summary>Furthest the tip may be placed, as a fraction of ball radius.</summary>
    public const float MaxOffset = 0.45f;

    /// <summary>Sideways tip offset (+ = left english), fraction of R.</summary>
    public float SpinSide { get; private set; }

    /// <summary>Vertical tip offset (+ = follow, − = draw), fraction of R.</summary>
    public float SpinVert { get; private set; }

    public event Action? Changed;

    private bool _dragging;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(132, 132);
        TooltipText = "Drag to set spin — top = follow, bottom = draw, sides = english";
    }

    public void SetSpin(float side, float vert, bool notify = true)
    {
        var len = Mathf.Sqrt(side * side + vert * vert);
        if (len > MaxOffset)
        {
            side *= MaxOffset / len;
            vert *= MaxOffset / len;
        }
        SpinSide = side;
        SpinVert = vert;
        QueueRedraw();
        if (notify)
            Changed?.Invoke();
    }

    public void Reset() => SetSpin(0f, 0f);

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed)
                {
                    _dragging = true;
                    ApplyFromPoint(mb.Position);
                }
                else
                {
                    _dragging = false;
                }
                AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                Reset();
                AcceptEvent();
                break;

            case InputEventMouseMotion mm when _dragging:
                ApplyFromPoint(mm.Position);
                AcceptEvent();
                break;
        }
    }

    private void ApplyFromPoint(Vector2 local)
    {
        var radius = BallRadius();
        var centre = Size * 0.5f;
        var d = (local - centre) / radius;
        // Screen Y grows downward; dragging up must mean follow.
        SetSpin(d.X, -d.Y);
    }

    private float BallRadius() => Mathf.Min(Size.X, Size.Y) * 0.5f - 8f;

    public override void _Draw()
    {
        var centre = Size * 0.5f;
        var r = BallRadius();

        // The ball.
        DrawCircle(centre, r, new Color(0.90f, 0.88f, 0.83f));
        DrawArc(centre, r, 0f, Mathf.Tau, 48, new Color(0.35f, 0.33f, 0.30f), 2f, true);

        // Shading so it reads as a sphere rather than a flat disc.
        DrawCircle(centre + new Vector2(-r * 0.28f, -r * 0.30f), r * 0.55f, new Color(1f, 1f, 1f, 0.16f));
        DrawCircle(centre + new Vector2(r * 0.30f, r * 0.34f), r * 0.62f, new Color(0f, 0f, 0f, 0.13f));

        // Cross hairs.
        var hair = new Color(0.45f, 0.43f, 0.40f, 0.55f);
        DrawLine(centre - new Vector2(r, 0), centre + new Vector2(r, 0), hair, 1f);
        DrawLine(centre - new Vector2(0, r), centre + new Vector2(0, r), hair, 1f);

        // Miscue limit.
        DrawArc(centre, r * MaxOffset, 0f, Mathf.Tau, 40, new Color(0.88f, 0.42f, 0.28f, 0.75f), 1.5f, true);

        // The chosen contact point.
        var dot = centre + new Vector2(SpinSide, -SpinVert) * r;
        DrawCircle(dot, 7f, new Color(0.15f, 0.15f, 0.17f, 0.85f));
        DrawCircle(dot, 5f, UiTheme.Accent);
        if (SpinSide != 0f || SpinVert != 0f)
            DrawLine(centre, dot, new Color(UiTheme.Accent, 0.5f), 1.5f);
    }
}
