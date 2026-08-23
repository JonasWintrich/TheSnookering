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

        // Hero mesh (Blender-built from the same TableSpec JSON) replaces the
        // procedural body when present; procedural stays as the fallback.
        var heroPath = spec.Snooker is null
            ? "res://assets/models/table_pool.glb"
            : "res://assets/models/table_snooker.glb";
        if (ResourceLoader.Exists(heroPath))
        {
            var hero = GD.Load<PackedScene>(heroPath).Instantiate<Node3D>();
            hero.Name = "HeroTable";
            RemapMaterials(hero);
            root.AddChild(hero);
            AddMarkings(root, spec, hw);
            return root;
        }

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

        // ---- pockets: a pitch-black hole at cloth level plus a leather casting
        // ARC that wraps only the OUTER side of the opening up to rail height —
        // the playfield side stays fully open so balls roll (and are seen to
        // roll) straight in, like a real table.
        pocketHole.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        pocketHole.AlbedoColor = new Color(0.004f, 0.004f, 0.005f);
        foreach (var pocket in spec.Pockets)
        {
            var fall = SimWorld.ToWorld(pocket.FallCenter);
            var holeR = (float)pocket.FallRadius + 0.008f;

            root.AddChild(new MeshInstance3D
            {
                Name = $"Pocket{pocket.Id}",
                Mesh = new CylinderMesh { TopRadius = holeR, BottomRadius = holeR, Height = 0.003f },
                Position = fall + new Vector3(0f, 0.0016f, 0f),
                MaterialOverride = pocketHole,
            });

            // Outward direction (sim space): away from the playfield.
            var isSide = Mathf.Abs((float)pocket.FallCenter.X) < 0.2f;
            var outAngle = isSide
                ? Mathf.Atan2(Mathf.Sign((float)pocket.FallCenter.Y), 0f)
                : Mathf.Atan2(Mathf.Sign((float)pocket.FallCenter.Y) * 0.70710678f,
                              Mathf.Sign((float)pocket.FallCenter.X) * 0.70710678f);

            // Casting wall: chord boxes along the outer ~210° arc, from below deck
            // up to just under rail height.
            const int wallBoxes = 7;
            var halfSpan = Mathf.DegToRad(105f);
            var wallR = holeR + 0.006f;
            const float wallTop = FrameTop - 0.004f;
            const float wallBottom = -0.06f;
            for (var s = 0; s < wallBoxes; s++)
            {
                var a = outAngle - halfSpan + 2f * halfSpan * (s + 0.5f) / wallBoxes;
                var radialSim = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                var chord = 2f * wallR * Mathf.Sin(halfSpan / wallBoxes) + 0.004f;

                var box = new MeshInstance3D
                {
                    Name = $"PocketWall{pocket.Id}_{s}",
                    Mesh = new BoxMesh { Size = new Vector3(chord, wallTop - wallBottom, 0.012f) },
                    MaterialOverride = leather,
                };
                var tangent = new Vector3(radialSim.Z, 0f, -radialSim.X);
                box.Basis = new Basis(tangent, Vector3.Up, radialSim);
                box.Position = fall + radialSim * wallR + new Vector3(0f, (wallTop + wallBottom) / 2f, 0f);
                root.AddChild(box);
            }
        }

        AddMarkings(root, spec, hw);
        return root;
    }

    /// <summary>Replace imported GLB materials with the richer runtime versions, by name.</summary>
    private static void RemapMaterials(Node3D hero)
    {
        var map = RuntimeMaterials();
        foreach (var node in hero.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
        {
            if (node is not MeshInstance3D mi || mi.Mesh is null)
                continue;
            for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var name = mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "";
                if (map.TryGetValue(name, out var replacement))
                    mi.SetSurfaceOverrideMaterial(s, replacement);
            }
        }
    }

    private static System.Collections.Generic.Dictionary<string, Material> RuntimeMaterials()
    {
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
        var cushion = (StandardMaterial3D)cloth.Duplicate();
        cushion.AlbedoColor = new Color(0.04f, 0.33f, 0.135f);

        var lineCloth = (StandardMaterial3D)cloth.Duplicate();
        lineCloth.AlbedoColor = new Color(0.55f, 0.6f, 0.5f);

        return new System.Collections.Generic.Dictionary<string, Material>
        {
            // Generated hero tables (make_table.py).
            ["Cloth"] = cloth,
            ["CushionCloth"] = cushion,
            // OpenGameArt tournament table (import_table.py, CC-BY BrightRetro).
            ["Beize"] = cloth,
            ["BeizeSides"] = cushion,
            ["BeizeCushions"] = cushion,
            ["DlinePool"] = lineCloth,
            ["TableWood"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.15f, 0.075f, 0.038f),
                Roughness = 0.32f,
                ClearcoatEnabled = true,
                Clearcoat = 0.35f,
                ClearcoatRoughness = 0.2f,
            },
            ["MetalFrame"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.18f, 0.16f, 0.14f),
                Metallic = 0.8f,
                Roughness = 0.45f,
            },
            ["Black"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.03f, 0.03f, 0.032f),
                Roughness = 0.7f,
            },
            ["PocketBlack"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.015f, 0.015f, 0.018f),
                Roughness = 0.9f,
            },
            ["Wood"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.08f, 0.04f),
                Roughness = 0.24f,
                ClearcoatEnabled = true,
                Clearcoat = 0.6f,
                ClearcoatRoughness = 0.15f,
            },
            ["DarkWood"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.13f, 0.065f, 0.035f),
                Roughness = 0.4f,
            },
            ["Leather"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.045f, 0.035f, 0.03f),
                Roughness = 0.55f,
            },
            ["Hole"] = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.004f, 0.004f, 0.005f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
    }

    /// <summary>Baulk line, D and spots (snooker) — drawn on top of either table body.</summary>
    private static void AddMarkings(Node3D root, TableSpec spec, float hw)
    {
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
    }
}
