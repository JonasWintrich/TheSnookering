using Snookering.Core.Mathematics;

namespace Snookering.Core.Tables;

/// <summary>
/// A straight cushion face. N is the unit normal pointing INTO the playfield;
/// balls collide with the face from the N side. Endpoints also act as collision
/// points (cushion noses).
/// </summary>
public readonly struct CushionSegment
{
    public readonly Vec2 A;
    public readonly Vec2 B;
    public readonly Vec2 N;
    public readonly short FeatureId;

    public CushionSegment(Vec2 a, Vec2 b, Vec2 n, short featureId)
    {
        A = a;
        B = b;
        N = n;
        FeatureId = featureId;
    }

    public Vec2 Dir => (B - A).Normalized();
    public double Length => (B - A).Length;
}

/// <summary>
/// A convex arc obstacle (curved pocket jaw / cushion nose). Balls collide with the
/// outside of the circle. The active angular range is stored trig-free as two edge
/// unit vectors: a contact direction u (from Center) is inside the wedge when
/// cross(StartDir, u) ≥ 0 AND cross(u, EndDir) ≥ 0 (arcs must span ≤ 180°).
/// </summary>
public readonly struct JawArc
{
    public readonly Vec2 Center;
    public readonly double Radius;
    public readonly Vec2 StartDir;
    public readonly Vec2 EndDir;
    public readonly short FeatureId;

    public JawArc(Vec2 center, double radius, Vec2 startDir, Vec2 endDir, short featureId)
    {
        Center = center;
        Radius = radius;
        StartDir = startDir;
        EndDir = endDir;
        FeatureId = featureId;
    }

    public bool ContainsDirection(Vec2 u) => StartDir.Cross(u) >= 0.0 && u.Cross(EndDir) >= 0.0;
}

/// <summary>A pocket's capture region: when a ball's CENTER enters the fall circle it is pocketed.</summary>
public readonly struct Pocket
{
    public readonly Vec2 FallCenter;
    public readonly double FallRadius;
    public readonly short Id;

    public Pocket(Vec2 fallCenter, double fallRadius, short id)
    {
        FallCenter = fallCenter;
        FallRadius = fallRadius;
        Id = id;
    }
}
