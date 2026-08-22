using System.Collections.Generic;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;

namespace Snookering.Core.Tables;

/// <summary>
/// Complete analytic description of one table: playfield extents, cushion faces,
/// jaw arcs, and pocket capture circles. Coordinates: origin at table center,
/// X along the long axis, Y across. This data drives BOTH the physics and the
/// generated visual mesh, so they can never drift apart.
/// </summary>
public sealed class TableSpec
{
    /// <summary>Playfield half-extents (nose-to-nose / 2), meters.</summary>
    public required double HalfLength { get; init; }

    public required double HalfWidth { get; init; }

    public required PhysicsParams Physics { get; init; }

    public required IReadOnlyList<CushionSegment> Cushions { get; init; }

    public required IReadOnlyList<JawArc> Jaws { get; init; }

    public required IReadOnlyList<Pocket> Pockets { get; init; }

    public required string Name { get; init; }

    /// <summary>Snooker spot/D data; null on pool tables.</summary>
    public SnookerSpots? Snooker { get; init; }

    /// <summary>Y of the head string (ball-in-hand zone boundary), pool convention: quarter table.</summary>
    public double HeadStringX => HalfLength / 2.0;

    public static TableSpec Pool9ft() => PoolTableFactory.Build();

    public static TableSpec Snooker12ft() => SnookerTableFactory.Build();
}
