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

        // ---- floor: real wood parquet (ambientCG WoodFloor043, CC0)
        var floorMat = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/floor/color.jpg"),
            AlbedoColor = new Color(0.55f, 0.5f, 0.45f), // dimmed toward the moody palette
            NormalEnabled = true,
            NormalTexture = GD.Load<Texture2D>("res://assets/textures/floor/normal.jpg"),
            RoughnessTexture = GD.Load<Texture2D>("res://assets/textures/floor/roughness.jpg"),
            Roughness = 1f,
            Uv1Scale = new Vector3(7f, 6f, 1f),
        };
        root.AddChild(new MeshInstance3D
        {
            Name = "Floor",
            Mesh = new PlaneMesh { Size = new Vector2(12f, 10f) },
            Position = new Vector3(0f, FloorY, 0f),
            MaterialOverride = floorMat,
        });

        // ---- rug under the table: anchors it visually and owns the light spill
        root.AddChild(new MeshInstance3D
        {
            Name = "Rug",
            Mesh = new BoxMesh { Size = new Vector3(2f * tableHalfLength + 2.2f, 0.012f, 2f * tableHalfWidth + 2.2f) },
            Position = new Vector3(0f, FloorY + 0.006f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/carpet/color.jpg"),
                AlbedoColor = new Color(0.5f, 0.22f, 0.18f), // tint the carpet toward deep red
                NormalEnabled = true,
                NormalTexture = GD.Load<Texture2D>("res://assets/textures/carpet/normal.jpg"),
                Roughness = 1f,
                Uv1Scale = new Vector3(4f, 3f, 1f),
            },
        });

        // ---- walls: warm plaster above dark wainscot paneling
        var plaster = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.30f, 0.24f, 0.18f),
            Roughness = 0.92f,
        };
        var wainscot = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.11f, 0.06f, 0.035f),
            Roughness = 0.45f,
            ClearcoatEnabled = true,
            Clearcoat = 0.3f,
        };
        const float wainscotH = 1.1f;
        const float wallH = 3.4f;
        void Wall(string name, bool alongX, float wallPos)
        {
            var length = alongX ? 12f : 10f;
            var lower = new MeshInstance3D
            {
                Name = name + "Wainscot",
                Mesh = new BoxMesh
                {
                    Size = alongX ? new Vector3(length, wainscotH, 0.12f) : new Vector3(0.12f, wainscotH, length),
                },
                Position = alongX
                    ? new Vector3(0f, FloorY + wainscotH / 2f, wallPos)
                    : new Vector3(wallPos, FloorY + wainscotH / 2f, 0f),
                MaterialOverride = wainscot,
            };
            root.AddChild(lower);
            var upper = new MeshInstance3D
            {
                Name = name,
                Mesh = new BoxMesh
                {
                    Size = alongX ? new Vector3(length, wallH - wainscotH, 0.1f) : new Vector3(0.1f, wallH - wainscotH, length),
                },
                Position = alongX
                    ? new Vector3(0f, FloorY + wainscotH + (wallH - wainscotH) / 2f, wallPos)
                    : new Vector3(wallPos, FloorY + wainscotH + (wallH - wainscotH) / 2f, 0f),
                MaterialOverride = plaster,
            };
            root.AddChild(upper);
        }
        Wall("WallN", true, -5f);
        Wall("WallS", true, 5f);
        Wall("WallE", false, 6f);
        Wall("WallW", false, -6f);
        root.AddChild(new MeshInstance3D
        {
            Name = "Ceiling",
            Mesh = new BoxMesh { Size = new Vector3(12f, 0.1f, 10f) },
            Position = new Vector3(0f, FloorY + wallH, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.08f, 0.065f, 0.05f),
                Roughness = 0.95f,
            },
        });

        BuildProps(root);

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

    /// <summary>Wall dressing: sconces (with their own dim lights), bar, art, cue rack.</summary>
    private static void BuildProps(Node3D root)
    {
        var brass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.42f, 0.18f),
            Metallic = 0.9f,
            Roughness = 0.35f,
        };
        var darkWood = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 0.055f, 0.03f),
            Roughness = 0.4f,
            ClearcoatEnabled = true,
            Clearcoat = 0.3f,
        };
        var counterTop = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.04f, 0.035f),
            Roughness = 0.2f,
            ClearcoatEnabled = true,
            Clearcoat = 0.8f,
            ClearcoatRoughness = 0.1f,
        };

        // ---- wall sconces: warm glow strips that make the room READ in the dark
        void Sconce(string name, Vector3 pos, float yawDeg)
        {
            var sconce = new Node3D { Name = name, Position = pos, RotationDegrees = new Vector3(0f, yawDeg, 0f) };
            sconce.AddChild(new MeshInstance3D
            {
                Name = "Shade",
                Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.075f, Height = 0.16f },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.9f, 0.78f, 0.55f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.8f, 0.5f),
                    EmissionEnergyMultiplier = 1.4f,
                    Roughness = 0.8f,
                },
            });
            sconce.AddChild(new OmniLight3D
            {
                Name = "Light",
                LightColor = new Color(1f, 0.8f, 0.55f),
                LightEnergy = 0.5f,
                OmniRange = 2.6f,
                OmniAttenuation = 1.6f,
                ShadowEnabled = false,
            });
            root.AddChild(sconce);
        }
        Sconce("SconceN1", new Vector3(-3.2f, FloorY + 1.9f, -4.85f), 0f);
        Sconce("SconceN2", new Vector3(3.2f, FloorY + 1.9f, -4.85f), 0f);
        Sconce("SconceS1", new Vector3(-3.2f, FloorY + 1.9f, 4.85f), 0f);
        Sconce("SconceS2", new Vector3(3.2f, FloorY + 1.9f, 4.85f), 0f);
        Sconce("SconceE", new Vector3(5.85f, FloorY + 1.9f, 0f), 0f);
        Sconce("SconceW", new Vector3(-5.85f, FloorY + 1.9f, 0f), 0f);

        // ---- bar counter along the north wall
        var bar = new Node3D { Name = "Bar", Position = new Vector3(0f, 0f, -4.35f) };
        bar.AddChild(new MeshInstance3D
        {
            Name = "Body",
            Mesh = new BoxMesh { Size = new Vector3(4.6f, 1.05f, 0.6f) },
            Position = new Vector3(0f, FloorY + 0.525f, 0f),
            MaterialOverride = darkWood,
        });
        bar.AddChild(new MeshInstance3D
        {
            Name = "Top",
            Mesh = new BoxMesh { Size = new Vector3(4.8f, 0.05f, 0.75f) },
            Position = new Vector3(0f, FloorY + 1.08f, 0f),
            MaterialOverride = counterTop,
        });
        bar.AddChild(new MeshInstance3D
        {
            Name = "FootRail",
            Mesh = new BoxMesh { Size = new Vector3(4.4f, 0.03f, 0.03f) },
            Position = new Vector3(0f, FloorY + 0.16f, 0.34f),
            MaterialOverride = brass,
        });
        root.AddChild(bar);

        // ---- framed pictures on the south wall
        var frameMat = new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.05f, 0.03f), Roughness = 0.4f };
        var artColors = new[]
        {
            new Color(0.25f, 0.16f, 0.08f),
            new Color(0.12f, 0.16f, 0.14f),
            new Color(0.2f, 0.1f, 0.09f),
        };
        for (var i = 0; i < 3; i++)
        {
            var x = -2.2f + 2.2f * i;
            root.AddChild(new MeshInstance3D
            {
                Name = $"Frame{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.85f, 1.1f, 0.05f) },
                Position = new Vector3(x, FloorY + 1.95f, 4.9f),
                MaterialOverride = frameMat,
            });
            root.AddChild(new MeshInstance3D
            {
                Name = $"Art{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.95f, 0.02f) },
                Position = new Vector3(x, FloorY + 1.95f, 4.87f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = artColors[i], Roughness = 0.9f },
            });
        }

        // ---- photoscanned props (Poly Haven, CC0)
        Node3D? Prop(string slug, Vector3 pos, float yawDeg, float scale = 1f)
        {
            var path = $"res://assets/models/props/{slug}/{slug}_1k.gltf";
            if (!ResourceLoader.Exists(path))
                return null;
            var n = GD.Load<PackedScene>(path).Instantiate<Node3D>();
            n.Name = slug;
            n.Position = pos;
            n.RotationDegrees = new Vector3(0f, yawDeg, 0f);
            n.Scale = Vector3.One * scale;
            root.AddChild(n);
            return n;
        }

        // Reading corner: two leather armchairs around a coffee table with a chess set.
        Prop("ArmChair_01", new Vector3(-4.4f, FloorY, 3.3f), 155f);
        Prop("ArmChair_01", new Vector3(-2.9f, FloorY, 4.1f), 195f);
        Prop("CoffeeTable_01", new Vector3(-3.7f, FloorY, 3.6f), 15f);
        Prop("chess_set", new Vector3(-3.7f, FloorY + 0.42f, 3.6f), 40f, 0.75f);

        // Bar stools in front of the counter.
        Prop("bar_chair_round_01", new Vector3(-1.3f, FloorY, -3.75f), 170f);
        Prop("bar_chair_round_01", new Vector3(0f, FloorY, -3.7f), 200f);
        Prop("bar_chair_round_01", new Vector3(1.3f, FloorY, -3.78f), 185f);

        // Dartboard on the east wall, plant in the corner, shelf on the west wall.
        var dart = Prop("dartboard", new Vector3(5.88f, FloorY + 1.75f, -1.6f), -90f);
        Prop("calathea_orbifolia_01", new Vector3(5.3f, FloorY, 4.2f), 0f, 1.2f);
        var shelf = Prop("Shelf_01", new Vector3(-5.85f, FloorY + 1.45f, -2.2f), 90f);

        // ---- ambient people (Quaternius animated characters, CC0):
        // one leaning at the bar, one sitting in the reading corner. They idle,
        // fidget occasionally, and clap on good pots (see NpcView).
        root.AddChild(NpcView.Create("res://assets/models/npc/suit_man.glb",
            new Vector3(0.9f, FloorY, -3.35f), 10f, basePrefix: "Idle", scale: 0.9f));
        root.AddChild(NpcView.Create("res://assets/models/npc/casual_man.glb",
            new Vector3(-1.6f, FloorY, -3.6f), 195f, basePrefix: "Idle", scale: 0.9f));
        root.AddChild(NpcView.Create("res://assets/models/npc/formal_woman.glb",
            new Vector3(-3.4f, FloorY, 2.9f), 205f, basePrefix: "Idle", scale: 0.9f));

        // ---- cue rack on the west wall
        var rack = new Node3D { Name = "CueRack", Position = new Vector3(-5.9f, 0f, 1.8f) };
        rack.AddChild(new MeshInstance3D
        {
            Name = "Board",
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 1.7f, 0.7f) },
            Position = new Vector3(0f, FloorY + 1.5f, 0f),
            MaterialOverride = darkWood,
        });
        var cueWood = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.45f, 0.28f), Roughness = 0.4f };
        for (var i = 0; i < 4; i++)
        {
            rack.AddChild(new MeshInstance3D
            {
                Name = $"RackCue{i}",
                Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.013f, Height = 1.45f },
                Position = new Vector3(0.05f, FloorY + 1.45f, -0.24f + 0.16f * i),
                MaterialOverride = cueWood,
            });
        }
        root.AddChild(rack);
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
