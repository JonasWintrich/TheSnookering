using Godot;
using Snookering.Core.Tables;

namespace Snookering.Game.Render;

/// <summary>
/// Generates gray-box table visuals directly from the physics TableSpec, so what
/// you see IS what the simulation collides with. The M3 beauty mesh replaces the
/// looks, never the geometry source.
/// </summary>
public static class TableBuilder
{
    public const float CushionHeight = 0.045f;
    public const float CushionBack = 0.06f;
    public const float BedMargin = 0.12f;

    public static Node3D Build(TableSpec spec)
    {
        var root = new Node3D { Name = "Table" };

        var cloth = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.42f, 0.18f),
            Roughness = 0.95f,
        };
        var cushionMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.32f, 0.14f),
            Roughness = 0.9f,
        };
        var pocketMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.03f, 0.03f, 0.03f),
            Roughness = 0.6f,
        };

        // Bed: a slab whose top surface is the playfield plane (y = 0).
        var bed = new MeshInstance3D
        {
            Name = "Bed",
            Mesh = new BoxMesh
            {
                Size = new Vector3(
                    2f * (float)spec.HalfLength + 2f * BedMargin,
                    0.04f,
                    2f * (float)spec.HalfWidth + 2f * BedMargin),
            },
            Position = new Vector3(0f, -0.02f, 0f),
            MaterialOverride = cloth,
        };
        root.AddChild(bed);

        // Cushions: one box per physics segment, its inner face exactly on the segment.
        var i = 0;
        foreach (var seg in spec.Cushions)
        {
            var a = SimWorld.ToWorld(seg.A);
            var b = SimWorld.ToWorld(seg.B);
            var n = SimWorld.ToWorld(seg.N); // horizontal, into the playfield
            var mid = (a + b) * 0.5f;
            var len = (b - a).Length();

            var box = new MeshInstance3D
            {
                Name = $"Cushion{i++}",
                Mesh = new BoxMesh { Size = new Vector3(len, CushionHeight, CushionBack) },
                MaterialOverride = cushionMat,
            };

            // Local axes: X along the segment, Z along the outward normal (−n).
            var xAxis = (b - a).Normalized();
            var zAxis = -n;
            var yAxis = zAxis.Cross(xAxis);
            box.Basis = new Basis(xAxis, yAxis, zAxis);
            box.Position = mid - n * (CushionBack * 0.5f) + new Vector3(0f, CushionHeight * 0.5f, 0f);
            root.AddChild(box);
        }

        // Jaw arcs (snooker): approximate each arc with short chord boxes so the
        // visible wall matches the physics circle.
        var arcIdx = 0;
        foreach (var arc in spec.Jaws)
        {
            var a0 = Mathf.Atan2((float)arc.StartDir.Y, (float)arc.StartDir.X);
            var a1 = Mathf.Atan2((float)arc.EndDir.Y, (float)arc.EndDir.X);
            var sweep = Mathf.Wrap(a1 - a0, 0f, Mathf.Tau); // Start→End is CCW in sim space
            const int chords = 4;
            const float thickness = 0.02f;

            for (var s = 0; s < chords; s++)
            {
                var am = a0 + sweep * (s + 0.5f) / chords;
                var radial = new Godot.Vector3(Mathf.Cos(am), 0f, -Mathf.Sin(am)); // sim→world
                var centerWorld = SimWorld.ToWorld(arc.Center) + radial * ((float)arc.Radius + thickness * 0.5f);
                var chordLen = 2f * (float)arc.Radius * Mathf.Sin(sweep / (2 * chords)) + 0.006f;

                var box = new MeshInstance3D
                {
                    Name = $"Jaw{arcIdx}_{s}",
                    Mesh = new BoxMesh { Size = new Vector3(chordLen, CushionHeight, thickness) },
                    MaterialOverride = cushionMat,
                };
                var tangent = new Vector3(radial.Z, 0f, -radial.X);
                box.Basis = new Basis(tangent, Vector3.Up, radial);
                box.Position = centerWorld + new Vector3(0f, CushionHeight * 0.5f, 0f);
                root.AddChild(box);
            }
            arcIdx++;
        }

        // Baulk line + D (snooker).
        if (spec.Snooker is { } spots)
        {
            var lineMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.85f, 0.85f, 0.8f),
                Roughness = 1f,
            };
            var baulk = new MeshInstance3D
            {
                Name = "BaulkLine",
                Mesh = new BoxMesh { Size = new Vector3(0.004f, 0.0012f, 2f * (float)spec.HalfWidth) },
                Position = new Vector3((float)spots.BaulkX, 0.0011f, 0f),
                MaterialOverride = lineMat,
            };
            root.AddChild(baulk);

            const int dSegs = 16;
            for (var s = 0; s < dSegs; s++)
            {
                // Semicircle on the baulk side (−X half).
                var ang = Mathf.Pi / 2f + Mathf.Pi * (s + 0.5f) / dSegs;
                var mid = SimWorld.ToWorld(spots.DCenter, 0.0011f)
                          + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (float)spots.DRadiusValue;
                var seg = new MeshInstance3D
                {
                    Name = $"D{s}",
                    Mesh = new BoxMesh
                    {
                        Size = new Vector3(2f * (float)spots.DRadiusValue * Mathf.Sin(Mathf.Pi / (2 * dSegs)) + 0.002f, 0.0012f, 0.004f),
                    },
                    Position = mid,
                    MaterialOverride = lineMat,
                };
                seg.RotateY(-ang - Mathf.Pi / 2f);
                root.AddChild(seg);
            }
        }

        // Pockets: dark discs marking the fall circles.
        foreach (var pocket in spec.Pockets)
        {
            var disc = new MeshInstance3D
            {
                Name = $"Pocket{pocket.Id}",
                Mesh = new CylinderMesh
                {
                    TopRadius = (float)pocket.FallRadius,
                    BottomRadius = (float)pocket.FallRadius,
                    Height = 0.006f,
                },
                Position = SimWorld.ToWorld(pocket.FallCenter, 0.004f),
                MaterialOverride = pocketMat,
            };
            root.AddChild(disc);
        }

        return root;
    }
}
