using Snookering.Core.Mathematics;

namespace Snookering.Core.Physics;

public enum MotionState : byte
{
    Stationary = 0,
    Sliding = 1,
    Rolling = 2,
}

/// <summary>
/// Full dynamic state of one ball on the 2D table plane.
/// Angular velocity is 3D: X/Y are horizontal spin axes (follow/draw and their
/// perpendicular), Z is vertical spin (english).
/// </summary>
public struct BallState
{
    public byte Id;
    public Vec2 Pos;
    public Vec2 Vel;
    public Vec3 AngVel;
    public MotionState State;
    public bool OnTable;

    /// <summary>Moving or still spinning in place — the shot is not over while any ball is active.</summary>
    public readonly bool IsActive => OnTable && (State != MotionState.Stationary || AngVel.Z != 0.0);

    public static BallState AtRest(byte id, Vec2 pos) => new()
    {
        Id = id,
        Pos = pos,
        Vel = Vec2.Zero,
        AngVel = Vec3.Zero,
        State = MotionState.Stationary,
        OnTable = true,
    };
}
