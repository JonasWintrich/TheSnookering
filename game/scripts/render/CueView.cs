using Godot;

namespace Snookering.Game.Render;

/// <summary>
/// The visible cue stick. Points along the aim, its tip mirrors the selected spin
/// offset on the cue ball, pulls back while power is charged, and lunges forward
/// during the strike animation. Pure presentation.
/// </summary>
public partial class CueView : Node3D
{
    private const float Length = 1.45f;
    private const float TipGap = 0.012f;
    private const float MaxPullback = 0.26f;
    private const float ElevationRad = 0.10f; // slight visual butt lift

    public static CueView Create()
    {
        var cue = new CueView { Name = "Cue" };

        var shaft = new MeshInstance3D
        {
            Name = "Shaft",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.0065f,   // tip end (cylinder +Y before rotation)
                BottomRadius = 0.015f, // butt end
                Height = Length,
                RadialSegments = 24,
            },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.78f, 0.62f, 0.42f),
                Roughness = 0.35f,
            },
        };
        // Cylinder is Y-aligned; rotate so +Y (tip) points along the node's −Z (forward).
        shaft.RotationDegrees = new Vector3(-90f, 0f, 0f);
        shaft.Position = new Vector3(0f, 0f, Length * 0.5f);
        cue.AddChild(shaft);

        var tip = new MeshInstance3D
        {
            Name = "Tip",
            Mesh = new CylinderMesh { TopRadius = 0.0062f, BottomRadius = 0.0065f, Height = 0.014f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.45f, 0.85f),
                Roughness = 0.9f,
            },
        };
        tip.RotationDegrees = new Vector3(-90f, 0f, 0f);
        tip.Position = new Vector3(0f, 0f, -0.007f);
        cue.AddChild(tip);

        var butt = new MeshInstance3D
        {
            Name = "Butt",
            Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.017f, Height = 0.42f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.09f, 0.06f),
                Roughness = 0.5f,
            },
        };
        butt.RotationDegrees = new Vector3(-90f, 0f, 0f);
        butt.Position = new Vector3(0f, 0f, Length + 0.21f);
        cue.AddChild(butt);

        return cue;
    }

    /// <summary>
    /// Place the cue for the current aim.
    /// pull: 0–1 charge (pulls back); strike: 0–1 lunge (overrides pull, drives tip to the ball).
    /// spinSide/spinVert: tip offset as fractions of R, mirroring the ShotInput offsets.
    /// </summary>
    public void Place(Vector3 ballCenter, float ballRadius, float aimYawWorld, float spinSide, float spinVert, float pull, float strike)
    {
        var dir = new Vector3(Mathf.Cos(aimYawWorld), 0f, -Mathf.Sin(aimYawWorld));
        var left = Vector3.Up.Cross(dir).Normalized();
        var strikeDir = (dir * Mathf.Cos(ElevationRad) - Vector3.Up * Mathf.Sin(ElevationRad)).Normalized();

        var back = TipGap + pull * MaxPullback;
        if (strike > 0f)
            back = Mathf.Lerp(back, 0.001f, strike);

        var tipPos = ballCenter
                     + left * (spinSide * ballRadius * 0.85f)
                     + Vector3.Up * (spinVert * ballRadius * 0.85f)
                     - strikeDir * (ballRadius + back);

        // Node forward (−Z) = strike direction; children put the tip at the node origin.
        Position = tipPos + strikeDir * 0.0f;
        LookAt(tipPos + strikeDir, Vector3.Up);
    }
}
