using Godot;
using CoreVec2 = Snookering.Core.Mathematics.Vec2;
using CoreVec3 = Snookering.Core.Mathematics.Vec3;

namespace Snookering.Game.Render;

/// <summary>
/// Coordinate mapping between the simulation plane and Godot world space.
/// Sim: X long axis, Y across, Z up (right-handed).
/// Godot: X right, Y up, Z toward viewer (right-handed).
/// Handedness-preserving map: (x, y, z)_sim → (x, z, −y)_godot.
/// </summary>
public static class SimWorld
{
    public static Vector3 ToWorld(CoreVec2 pos, float height = 0f) =>
        new((float)pos.X, height, (float)(-pos.Y));

    public static Vector3 ToWorld(CoreVec3 v) =>
        new((float)v.X, (float)v.Z, (float)(-v.Y));

    public static CoreVec2 ToSim(Vector3 world) => new(world.X, -world.Z);
}
