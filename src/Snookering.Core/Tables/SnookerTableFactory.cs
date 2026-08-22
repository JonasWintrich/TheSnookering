using System.Collections.Generic;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;

namespace Snookering.Core.Tables;

/// <summary>
/// The 12-ft WPBSA snooker table: 140.5"×70" playfield (3.569 × 1.778 m),
/// tight pockets with CURVED jaws (arcs tangent to the rail faces — balls that
/// catch them rattle out far more than on a pool table), baulk line + D, and
/// the six color spots.
/// </summary>
public static class SnookerTableFactory
{
    private const double HalfLength = 1.7845;
    private const double HalfWidth = 0.889;

    private const double CornerMouth = 0.086;
    private const double SideMouth = 0.103;
    private const double CornerCutback = CornerMouth * 0.7071067811865476;
    private const double SideCutback = SideMouth / 2.0;

    private const double CornerJawRadius = 0.050;
    private const double SideJawRadius = 0.035;

    private const double CornerFallOffset = 0.042;
    private const double CornerFallRadius = 0.046;
    private const double SideFallOffset = 0.031;
    private const double SideFallRadius = 0.043;

    // Baulk line 737 mm from the baulk cushion; D radius 292 mm; black spot 324 mm off the top cushion.
    public const double BaulkLineX = -HalfLength + 0.737;
    public const double DRadius = 0.292;

    private static readonly double[] Signs = { 1.0, -1.0 };

    public static SnookerSpots Spots => new()
    {
        Yellow = new Vec2(BaulkLineX, -DRadius),
        Green = new Vec2(BaulkLineX, DRadius),
        Brown = new Vec2(BaulkLineX, 0.0),
        Blue = new Vec2(0.0, 0.0),
        Pink = new Vec2(HalfLength / 2.0, 0.0),
        Black = new Vec2(HalfLength - 0.324, 0.0),
        DCenter = new Vec2(BaulkLineX, 0.0),
        DRadiusValue = DRadius,
        BaulkX = BaulkLineX,
    };

    public static TableSpec Build()
    {
        var cushions = new List<CushionSegment>();
        var jaws = new List<JawArc>();
        var pockets = new List<Pocket>();
        short feature = 200;

        // Long rails, split by the side pockets.
        foreach (var sy in Signs)
        {
            var y = sy * HalfWidth;
            var n = new Vec2(0.0, -sy);
            cushions.Add(new CushionSegment(
                new Vec2(-HalfLength + CornerCutback, y), new Vec2(-SideCutback, y), n, feature++));
            cushions.Add(new CushionSegment(
                new Vec2(SideCutback, y), new Vec2(HalfLength - CornerCutback, y), n, feature++));
        }

        // Short rails.
        foreach (var sx in Signs)
        {
            var x = sx * HalfLength;
            var n = new Vec2(-sx, 0.0);
            cushions.Add(new CushionSegment(
                new Vec2(x, -HalfWidth + CornerCutback), new Vec2(x, HalfWidth - CornerCutback), n, feature++));
        }

        // Corner pockets with curved jaws: each jaw arc is tangent to its rail face
        // at the nose (center sits behind the face by one jaw radius).
        short pocketId = 0;
        foreach (var sx in Signs)
        {
            foreach (var sy in Signs)
            {
                var corner = new Vec2(sx * HalfLength, sy * HalfWidth);
                var diag = new Vec2(sx * 0.7071067811865476, sy * 0.7071067811865476);
                var fall = corner + CornerFallOffset * diag;
                pockets.Add(new Pocket(fall, CornerFallRadius, pocketId++));

                // Jaw off the long rail (face y = ±HW, inward normal ∓y).
                var noseLong = new Vec2(sx * (HalfLength - CornerCutback), sy * HalfWidth);
                var centerLong = noseLong + new Vec2(0.0, sy * CornerJawRadius);
                jaws.Add(MakeArc(centerLong, CornerJawRadius, new Vec2(0.0, -sy), fall, feature++));

                // Jaw off the short rail (face x = ±HL, inward normal ∓x).
                var noseShort = new Vec2(sx * HalfLength, sy * (HalfWidth - CornerCutback));
                var centerShort = noseShort + new Vec2(sx * CornerJawRadius, 0.0);
                jaws.Add(MakeArc(centerShort, CornerJawRadius, new Vec2(-sx, 0.0), fall, feature++));
            }
        }

        // Side pockets on the long rails at x = 0.
        foreach (var sy in Signs)
        {
            var railY = sy * HalfWidth;
            var fall = new Vec2(0.0, railY + sy * SideFallOffset);
            pockets.Add(new Pocket(fall, SideFallRadius, pocketId++));

            foreach (var sx in Signs)
            {
                var nose = new Vec2(sx * SideCutback, railY);
                var center = nose + new Vec2(0.0, sy * SideJawRadius);
                jaws.Add(MakeArc(center, SideJawRadius, new Vec2(0.0, -sy), fall, feature++));
            }
        }

        return new TableSpec
        {
            Name = "Snooker 12ft (WPBSA)",
            HalfLength = HalfLength,
            HalfWidth = HalfWidth,
            Physics = PhysicsParams.Snooker(),
            Cushions = cushions,
            Jaws = jaws,
            Pockets = pockets,
            Snooker = Spots,
        };
    }

    /// <summary>
    /// Build a jaw arc spanning from the rail-tangency direction to the direction
    /// of the pocket fall center, auto-ordered CCW (ContainsDirection requires
    /// cross(Start,u) ≥ 0 ≥ cross(End,u)-style ordering; sweeps are &lt; 180°).
    /// </summary>
    private static JawArc MakeArc(Vec2 center, double radius, Vec2 tangentDir, Vec2 fallCenter, short featureId)
    {
        var throatDir = (fallCenter - center).Normalized();
        return tangentDir.Cross(throatDir) >= 0.0
            ? new JawArc(center, radius, tangentDir, throatDir, featureId)
            : new JawArc(center, radius, throatDir, tangentDir, featureId);
    }
}

/// <summary>Snooker spot positions and D geometry, carried by the snooker TableSpec.</summary>
public sealed class SnookerSpots
{
    public required Vec2 Yellow { get; init; }
    public required Vec2 Green { get; init; }
    public required Vec2 Brown { get; init; }
    public required Vec2 Blue { get; init; }
    public required Vec2 Pink { get; init; }
    public required Vec2 Black { get; init; }
    public required Vec2 DCenter { get; init; }
    public required double DRadiusValue { get; init; }
    public required double BaulkX { get; init; }

    public Vec2 SpotOf(byte colorId) => colorId switch
    {
        SnookerBalls.Yellow => Yellow,
        SnookerBalls.Green => Green,
        SnookerBalls.Brown => Brown,
        SnookerBalls.Blue => Blue,
        SnookerBalls.Pink => Pink,
        SnookerBalls.Black => Black,
        _ => Blue,
    };
}

/// <summary>Snooker ball id scheme: 0 = cue, 1–15 = reds, 16–21 = colors in value order.</summary>
public static class SnookerBalls
{
    public const byte Yellow = 16;
    public const byte Green = 17;
    public const byte Brown = 18;
    public const byte Blue = 19;
    public const byte Pink = 20;
    public const byte Black = 21;

    public static bool IsRed(byte id) => id >= 1 && id <= 15;
    public static bool IsColor(byte id) => id >= Yellow && id <= Black;

    /// <summary>Point value: red = 1, yellow = 2 … black = 7.</summary>
    public static int Value(byte id) => IsRed(id) ? 1 : IsColor(id) ? id - 14 : 0;
}
