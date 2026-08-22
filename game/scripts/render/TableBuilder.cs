using Godot;
using Snookering.Core.Tables;

namespace Snookering.Game.Render;

/// <summary>
/// Generates table visuals directly from the physics TableSpec, so what you see
/// IS what the simulation collides with. M3 look: felt cloth with a woven normal,
/// varnished wood rail frame + skirt + legs, brass-less gray-box pockets upgraded
/// to leather rings. (A Blender hero mesh may replace the looks later — never the
/// geometry source.)
/// </summary>
public static class TableBuilder
{
    public const float CushionHeight = 0.045f;
    public const float CushionBack = 0.05f;
    private const float RailWidth = 0.14f;
    private const float FrameTop = CushionHeight + 0.002f;

    public static Node3D Build(TableSpec spec)
    {
        var root = new Node3D { Name = "Table" };
        var hl = (float)spec.HalfLength;
        var hw = (float)spec.HalfWidth;

        // ---- materials -----------------------------------------------------
        var feltNoise = new NoiseTexture2D
        {
            Noise = new FastNoiseLite { Frequency = 0.35f, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth },
            AsNormalMap = true,
            BumpStrength = 1.6f,
            Width = 256,
            Height = 256,
            Seamless = true,
        };
        var cloth = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.045f, 0.36f, 0.15f),
            Roughness = 0.94f,
            NormalEnabled = true,
            NormalTexture = feltNoise,
            NormalScale = 0.5f,
            Uv1Scale = new Vector3(14f, 14f, 1f),
        };
        var clothCushion = (StandardMaterial3D)cloth.Duplicate();
        clothCushion.AlbedoColor = new Color(0.04f, 0.33f, 0.135f);

        var wood = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.08f, 0.04f),
            Roughness = 0.24f,
            ClearcoatEnabled = true,
            Clearcoat = 0.6f,
            ClearcoatRoughness = 0.15f,
        };
        var darkWood = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.13f, 0.065f, 0.035f),
            Roughness = 0.4f,
        };
        var leather = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.045f, 0.035f, 0.03f),
            Roughness = 0.55f,
        };
        var pocketHole = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.01f, 0.01f, 0.012f),
            Roughness = 1f,
        };

        // ---- bed (playfield cloth) ------------------------------------------
        root.AddChild(new MeshInstance3D
        {
            Name = "Bed",
            Mesh = new BoxMesh { Size = new Vector3(2f * hl + 0.10f, 0.04f, 2f * hw + 0.10f) },
            Position = new Vector3(0f, -0.02f, 0f),
            MaterialOverride = cloth,
        });

        // ---- cushions from physics segments ----------------------------------
        var i = 0;
        foreach (var seg in spec.Cushions)
        {
            var a = SimWorld.ToWorld(seg.A);
            var b = SimWorld.ToWorld(seg.B);
            var n = SimWorld.ToWorld(seg.N);
            var mid = (a + b) * 0.5f;
            var len = (b - a).Length();

            var box = new MeshInstance3D
            {
                Name = $"Cushion{i++}",
                Mesh = new BoxMesh { Size = new Vector3(len, CushionHeight, CushionBack) },
                MaterialOverride = clothCushion,
            };
            var xAxis = (b - a).Normalized();
            var zAxis = -n;
            box.Basis = new Basis(xAxis, zAxis.Cross(xAxis), zAxis);
            box.Position = mid - n * (CushionBack * 0.5f) + new Vector3(0f, CushionHeight * 0.5f, 0f);
            root.AddChild(box);
        }

        // ---- jaw arcs as chord boxes ------------------------------------------
        var arcIdx = 0;
        foreach (var arc in spec.Jaws)
        {
            var a0 = Mathf.Atan2((float)arc.StartDir.Y, (float)arc.StartDir.X);
            var a1 = Mathf.Atan2((float)arc.EndDir.Y, (float)arc.EndDir.X);
            var sweep = Mathf.Wrap(a1 - a0, 0f, Mathf.Tau);
            const int chords = 4;
            const float thickness = 0.018f;

            for (var s = 0; s < chords; s++)
            {
                var am = a0 + sweep * (s + 0.5f) / chords;
                var radial = new Vector3(Mathf.Cos(am), 0f, -Mathf.Sin(am));
                var centerWorld = SimWorld.ToWorld(arc.Center) + radial * ((float)arc.Radius + thickness * 0.5f);
                var chordLen = 2f * (float)arc.Radius * Mathf.Sin(sweep / (2 * chords)) + 0.006f;

                var box = new MeshInstance3D
                {
                    Name = $"Jaw{arcIdx}_{s}",
                    Mesh = new BoxMesh { Size = new Vector3(chordLen, CushionHeight, thickness) },
                    MaterialOverride = clothCushion,
                };
                var tangent = new Vector3(radial.Z, 0f, -radial.X);
                box.Basis = new Basis(tangent, Vector3.Up, radial);
                box.Position = centerWorld + new Vector3(0f, CushionHeight * 0.5f, 0f);
                root.AddChild(box);
            }
            arcIdx++;
        }

        // ---- wood frame: rails split at every pocket (real tables interrupt the
        // rail with a leather pocket casting; a continuous box through the corners
        // is what makes gray-box pockets look wrong).
        var innerL = hl + CushionBack;
        var innerW = hw + CushionBack;
        var frameH = 0.10f;
        var frameY = FrameTop - frameH / 2f;
        const float cornerTrim = 0.10f; // rails stop short of the corners
        var sideGap = spec.Pockets.Count > 4 ? 0.10f : 0.105f; // gap at the side pockets

        void Rail(string name, Vector3 size, Vector3 pos) => root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = wood,
        });

        var longSegLen = innerL - cornerTrim - sideGap;
        foreach (var sz in new[] { 1f, -1f })
        {
            var z = sz * (innerW + RailWidth / 2f);
            foreach (var sx in new[] { 1f, -1f })
            {
                var xc = sx * (sideGap + longSegLen / 2f);
                Rail($"Rail{sz}{sx}", new Vector3(longSegLen, frameH, RailWidth), new Vector3(xc, frameY, z));
            }
        }
        var shortSegLen = 2f * (innerW - cornerTrim);
        Rail("RailE", new Vector3(RailWidth, frameH, shortSegLen), new Vector3(innerL + RailWidth / 2f, frameY, 0f));
        Rail("RailW", new Vector3(RailWidth, frameH, shortSegLen), new Vector3(-(innerL + RailWidth / 2f), frameY, 0f));

        // ---- skirt + legs down to the floor -------------------------------------
        var skirtH = 0.16f;
        void Skirt(string name, Vector3 size, Vector3 pos) => root.AddChild(new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = darkWood,
        });
        var skirtY = FrameTop - frameH - skirtH / 2f;
        Skirt("SkirtN", new Vector3(2f * innerL, skirtH, 0.04f), new Vector3(0f, skirtY, -innerW));
        Skirt("SkirtS", new Vector3(2f * innerL, skirtH, 0.04f), new Vector3(0f, skirtY, innerW));
        Skirt("SkirtE", new Vector3(0.04f, skirtH, 2f * innerW), new Vector3(innerL, skirtY, 0f));
        Skirt("SkirtW", new Vector3(0.04f, skirtH, 2f * innerW), new Vector3(-innerL, skirtY, 0f));

        var legTop = FrameTop - frameH;
        var legH = legTop - EnvironmentBuilder.FloorY;
        foreach (var sx in new[] { 1f, -1f })
        {
            foreach (var sz in new[] { 1f, -1f })
            {
                root.AddChild(new MeshInstance3D
                {
                    Name = $"Leg{sx}{sz}",
                    Mesh = new BoxMesh { Size = new Vector3(0.11f, legH, 0.11f) },
                    Position = new Vector3(sx * (innerL - 0.10f), legTop - legH / 2f, sz * (innerW - 0.10f)),
                    MaterialOverride = darkWood,
                });
            }
        }

        // ---- pockets: leather casting cup in the rail gap + unshaded black hole ----
        pocketHole.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        pocketHole.AlbedoColor = new Color(0.004f, 0.004f, 0.005f);
        foreach (var pocket in spec.Pockets)
        {
            var fall = SimWorld.ToWorld(pocket.FallCenter);
            var cupR = (float)pocket.FallRadius + 0.026f;

            // Leather cup: a ring wall standing in the rail gap around the drop.
            root.AddChild(new MeshInstance3D
            {
                Name = $"PocketCup{pocket.Id}",
                Mesh = new CylinderMesh
                {
                    TopRadius = cupR,
                    BottomRadius = cupR * 0.94f,
                    Height = frameH,
                    RadialSegments = 24,
                },
                Position = fall + new Vector3(0f, FrameTop - frameH / 2f - 0.012f, 0f),
                MaterialOverride = leather,
            });

            // The hole: pitch-black unshaded disc slightly above the cup's top face.
            root.AddChild(new MeshInstance3D
            {
                Name = $"Pocket{pocket.Id}",
                Mesh = new CylinderMesh
                {
                    TopRadius = (float)pocket.FallRadius + 0.006f,
                    BottomRadius = (float)pocket.FallRadius + 0.006f,
                    Height = 0.004f,
                },
                Position = fall + new Vector3(0f, FrameTop - 0.010f, 0f),
                MaterialOverride = pocketHole,
            });
        }

        // ---- baulk line + D (snooker) ------------------------------------------
        if (spec.Snooker is { } spots)
        {
            var lineMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.8f, 0.8f, 0.75f),
                Roughness = 1f,
            };
            root.AddChild(new MeshInstance3D
            {
                Name = "BaulkLine",
                Mesh = new BoxMesh { Size = new Vector3(0.004f, 0.0012f, 2f * hw) },
                Position = new Vector3((float)spots.BaulkX, 0.0011f, 0f),
                MaterialOverride = lineMat,
            });

            const int dSegs = 24;
            for (var s = 0; s < dSegs; s++)
            {
                var ang = Mathf.Pi / 2f + Mathf.Pi * (s + 0.5f) / dSegs;
                var mid = SimWorld.ToWorld(spots.DCenter, 0.0011f)
                          + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (float)spots.DRadiusValue;
                var segMesh = new MeshInstance3D
                {
                    Name = $"D{s}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3(2f * (float)spots.DRadiusValue * Mathf.Sin(Mathf.Pi / (2 * dSegs)) + 0.002f, 0.0012f, 0.003f),
                    },
                    Position = mid,
                    MaterialOverride = lineMat,
                };
                segMesh.RotateY(-ang - Mathf.Pi / 2f);
                root.AddChild(segMesh);
            }

            // Spots.
            foreach (var spot in new[] { spots.Yellow, spots.Green, spots.Brown, spots.Blue, spots.Pink, spots.Black })
            {
                root.AddChild(new MeshInstance3D
                {
                    Name = "Spot",
                    Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.006f, Height = 0.001f },
                    Position = SimWorld.ToWorld(spot, 0.0012f),
                    MaterialOverride = lineMat,
                });
            }
        }

        return root;
    }
}
