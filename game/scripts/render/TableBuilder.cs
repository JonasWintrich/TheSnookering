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
