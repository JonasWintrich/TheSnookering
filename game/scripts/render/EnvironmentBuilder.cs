using Godot;

namespace Snookering.Game.Render;

/// <summary>
/// The billiards lounge around the table: floor, walls, pendant lamps (emissive
/// shades + warm shadowed lights, count scaled to table length), and a static
/// reflection probe over the playfield. Everything sized from the table spec so
/// pool and snooker each get a fitting rig.
/// </summary>
public static class EnvironmentBuilder
{
    public const float FloorY = -0.82f;
    private const float LampHeight = 1.22f; // hung a bit higher: clears the lowered playback view

    public static Node3D Build(float tableHalfLength, float tableHalfWidth)
    {
        var root = new Node3D { Name = "Lounge" };

        // ---- floor: dark wood planks tone
        var floorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.11f, 0.07f),
            Roughness = 0.38f,
        };
        root.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(12f, 10f) },
            Position = new Vector3(0f, FloorY, 0f),
            MaterialOverride = floorMat,
        });

        // ---- walls: dark paneling, far enough to fall into the fog/dark
        var wallMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.085f, 0.07f),
            Roughness = 0.85f,
        };
        void Wall(string name, Vector3 size, Vector3 pos) => root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = wallMat,
        });
        Wall("WallN", new Vector3(12f, 3.4f, 0.1f), new Vector3(0f, FloorY + 1.7f, -5f));
        Wall("WallS", new Vector3(12f, 3.4f, 0.1f), new Vector3(0f, FloorY + 1.7f, 5f));
        Wall("WallE", new Vector3(0.1f, 3.4f, 10f), new Vector3(6f, FloorY + 1.7f, 0f));
        Wall("WallW", new Vector3(0.1f, 3.4f, 10f), new Vector3(-6f, FloorY + 1.7f, 0f));
        root.AddChild(new MeshInstance3D
        {
            Name = "Ceiling",
            Mesh = new BoxMesh { Size = new Vector3(12f, 0.1f, 10f) },
            Position = new Vector3(0f, FloorY + 3.4f, 0f),
            MaterialOverride = wallMat,
        });

        // ---- pendant lamps over the table
        var lampCount = tableHalfLength > 1.5f ? 4 : 3;
        var span = tableHalfLength * 0.55f;
        for (var i = 0; i < lampCount; i++)
        {
            var x = lampCount == 1 ? 0f : -span + 2f * span * i / (lampCount - 1);
            root.AddChild(BuildLamp($"Lamp{i}", new Vector3(x, LampHeight, 0f)));
        }

        // ---- static reflection probe fitted around the table volume
        root.AddChild(new ReflectionProbe
        {
            Name = "TableProbe",
            Size = new Vector3(2f * tableHalfLength + 1.2f, 1.6f, 2f * tableHalfWidth + 1.2f),
            Position = new Vector3(0f, 0.5f, 0f),
            UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
            Intensity = 1f,
        });

        return root;
    }

    /// <summary>Lamp meshes live on render layer 2 so the top-down camera can cull them.</summary>
    public const uint LampLayer = 1u << 1;

    private static Node3D BuildLamp(string name, Vector3 pos)
    {
        var lamp = new Node3D { Name = name, Position = pos };

        // Cable from the ceiling.
        lamp.AddChild(new MeshInstance3D
        {
            Name = "Cable",
            Layers = LampLayer,
            Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.004f, Height = 1.4f },
            Position = new Vector3(0f, 0.72f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.05f, 0.05f),
                Roughness = 0.6f,
            },
        });

        // Conical shade: dark green outside, warm emissive inside face carried by
        // a small emissive disc at the mouth (cheap and reads perfectly).
        lamp.AddChild(new MeshInstance3D
        {
            Name = "Shade",
            Layers = LampLayer,
            Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.19f, Height = 0.16f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.06f, 0.14f, 0.09f),
                Roughness = 0.35f,
                Metallic = 0.2f,
            },
        });
        lamp.AddChild(new MeshInstance3D
        {
            Name = "Glow",
            Layers = LampLayer,
            Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.01f },
            Position = new Vector3(0f, -0.08f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.87f, 0.65f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.82f, 0.55f),
                EmissionEnergyMultiplier = 6f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        });

        lamp.AddChild(new OmniLight3D
        {
            Name = "Light",
            Position = new Vector3(0f, -0.12f, 0f),
            LightColor = new Color(1f, 0.83f, 0.6f),
            LightEnergy = 1.9f,
            OmniRange = 4.5f,
            OmniAttenuation = 1.4f,
            ShadowEnabled = true,
            LightSize = 0.12f, // soft shadow penumbra
        });

        return lamp;
    }
}
