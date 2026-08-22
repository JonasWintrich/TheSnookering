using Godot;
using Snookering.Core.Physics;

namespace Snookering.Game.Render;

/// <summary>
/// Visual for one ball. Position comes from trajectory playback; rolling rotation
/// is integrated presentation-side from the sim's angular velocity (cosmetic).
/// </summary>
public partial class BallView : MeshInstance3D
{
    public byte BallId { get; private set; }

    private float _radius;
    private bool _sinking;
    private float _sinkT;

    private static readonly Color[] PoolColors =
    {
        new(0.95f, 0.93f, 0.88f), // 0 cue
        new(0.95f, 0.77f, 0.06f), // 1 yellow
        new(0.10f, 0.25f, 0.75f), // 2 blue
        new(0.85f, 0.10f, 0.10f), // 3 red
        new(0.45f, 0.15f, 0.60f), // 4 purple
        new(0.95f, 0.45f, 0.08f), // 5 orange
        new(0.10f, 0.50f, 0.20f), // 6 green
        new(0.55f, 0.12f, 0.15f), // 7 maroon
        new(0.05f, 0.05f, 0.05f), // 8 black
    };

    public static Color PoolColor(byte id)
    {
        var stripe = id > 8;
        var baseColor = PoolColors[stripe ? id - 8 : id];
        return stripe ? baseColor.Lerp(Colors.White, 0.45f) : baseColor;
    }

    public static Color SnookerColor(byte id) => id switch
    {
        0 => new Color(0.95f, 0.93f, 0.88f),
        16 => new Color(0.95f, 0.82f, 0.10f), // yellow
        17 => new Color(0.06f, 0.42f, 0.16f), // green
        18 => new Color(0.48f, 0.28f, 0.12f), // brown
        19 => new Color(0.10f, 0.25f, 0.78f), // blue
        20 => new Color(0.94f, 0.55f, 0.65f), // pink
        21 => new Color(0.05f, 0.05f, 0.05f), // black
        _ => new Color(0.78f, 0.08f, 0.08f),  // reds
    };

    public override void _Ready() => AddToGroup("balls");

    public static BallView Create(byte id, float radius, Color color, string? texturePath = null)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = texturePath is null ? color : Colors.White,
            Roughness = 0.19f,
            MetallicSpecular = 0.32f,
            ClearcoatEnabled = true,
            Clearcoat = 0.45f,
            ClearcoatRoughness = 0.09f,
        };
        if (texturePath is not null)
            material.AlbedoTexture = GD.Load<Texture2D>(texturePath);

        return new BallView
        {
            Name = $"Ball{id}",
            BallId = id,
            _radius = radius,
            Mesh = new SphereMesh { Radius = radius, Height = 2f * radius, RadialSegments = 48, Rings = 24 },
            MaterialOverride = material,
        };
    }

    public void SetBaseColor(Color color) =>
        ((StandardMaterial3D)MaterialOverride).AlbedoColor = color;

    public void Apply(in BallSample sample, float dt)
    {
        if (!sample.OnTable)
        {
            if (Visible && !_sinking)
            {
                _sinking = true;
                _sinkT = 0f;
            }
            if (_sinking)
            {
                _sinkT += dt;
                var k = Mathf.Clamp(_sinkT / 0.25f, 0f, 1f);
                Position = new Vector3(Position.X, _radius * (1f - k) - 2.2f * _radius * k, Position.Z);
                if (k >= 1f)
                {
                    Visible = false;
                    _sinking = false;
                }
            }
            return;
        }

        Visible = true;
        _sinking = false;
        Position = SimWorld.ToWorld(sample.Pos, _radius);

        // Integrate orientation from angular velocity: axis in world space, angle = |ω|·dt.
        var w = SimWorld.ToWorld(sample.AngVel);
        var speed = w.Length();
        if (speed > 1e-4f && dt > 0f)
            GlobalRotate(w / speed, speed * dt);
    }

    /// <summary>Snap to a state sample without animation (rack setup, ball-in-hand).</summary>
    public void Snap(in BallSample sample)
    {
        Visible = sample.OnTable;
        _sinking = false;
        if (sample.OnTable)
            Position = SimWorld.ToWorld(sample.Pos, _radius);
    }
}
