using System.Collections.Generic;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;

namespace Snookering.Core.Tables;

/// <summary>
/// Builds the 9-ft WPA pool table: 100"×50" playfield (2.54 × 1.27 m nose-to-nose),
/// corner pocket mouths 117 mm, side mouths 130 mm. Jaws are straight cut segments
/// (angled cushion faces guiding into the pocket) — balls that catch them rattle
/// naturally because jaws use the same cushion physics as rails.
///
/// All geometry is generated symmetrically from a handful of tunable constants;
/// pocket "tightness" is tuned here, not in code elsewhere.
/// </summary>
public static class PoolTableFactory
{
    private const double HalfLength = 1.27;
    private const double HalfWidth = 0.635;

    private const double CornerMouth = 0.117;
    private const double SideMouth = 0.130;

    /// <summary>Rail cutback from the corner point along each rail: mouth measured across the diagonal.</summary>
    private const double CornerCutback = CornerMouth * 0.7071067811865476;

    private const double JawLength = 0.055;

    /// <summary>cos/sin of the corner jaw cut angle (45° from the rail line, opening outward).</summary>
    private const double CornerJawCos = 0.7071067811865476;
    private const double CornerJawSin = 0.7071067811865476;

    /// <summary>Side jaws are much steeper (≈30° from the rail line).</summary>
    private const double SideJawCos = 0.8660254037844387;
    private const double SideJawSin = 0.5;

    private const double CornerFallOffset = 0.055; // fall-circle center beyond the corner, along the diagonal
    private const double CornerFallRadius = 0.062;
    private const double SideFallOffset = 0.048;   // beyond the rail line, straight out
    private const double SideFallRadius = 0.057;

    private static readonly double[] Signs = { 1.0, -1.0 };

    public static TableSpec Build()
    {
        var cushions = new List<CushionSegment>();
        var pockets = new List<Pocket>();
        short feature = 100;

        // Long rails (top y=+HW, bottom y=−HW) are split by the side pocket at x=0.
        foreach (var sy in Signs)
        {
            var y = sy * HalfWidth;
            var n = new Vec2(0.0, -sy);
            cushions.Add(new CushionSegment(
                new Vec2(-HalfLength + CornerCutback, y), new Vec2(-SideMouth / 2.0, y), n, feature++));
            cushions.Add(new CushionSegment(
                new Vec2(SideMouth / 2.0, y), new Vec2(HalfLength - CornerCutback, y), n, feature++));
        }

        // Short rails (left x=−HL, right x=+HL), unbroken between the two corner cutbacks.
        foreach (var sx in Signs)
        {
            var x = sx * HalfLength;
            var n = new Vec2(-sx, 0.0);
            cushions.Add(new CushionSegment(
                new Vec2(x, -HalfWidth + CornerCutback), new Vec2(x, HalfWidth - CornerCutback), n, feature++));
        }

        // Corner pockets: 4 corners, each with two jaw segments (one per adjoining rail).
        short pocketId = 0;
        foreach (var sx in Signs)
        {
            foreach (var sy in Signs)
            {
                var corner = new Vec2(sx * HalfLength, sy * HalfWidth);
                var diag = new Vec2(sx * 0.7071067811865476, sy * 0.7071067811865476);
                pockets.Add(new Pocket(corner + CornerFallOffset * diag, CornerFallRadius, pocketId));

                // Jaw off the long rail: nose at (sx·(HL−cutback), sy·HW), running outward.
                var noseLong = new Vec2(sx * (HalfLength - CornerCutback), sy * HalfWidth);
                var jawDirLong = new Vec2(sx * CornerJawCos, sy * CornerJawSin);
                var jawNormalLong = new Vec2(sx * CornerJawSin, -sy * CornerJawCos);
                cushions.Add(new CushionSegment(noseLong, noseLong + JawLength * jawDirLong, jawNormalLong, feature++));

                // Jaw off the short rail: nose at (sx·HL, sy·(HW−cutback)).
                var noseShort = new Vec2(sx * HalfLength, sy * (HalfWidth - CornerCutback));
                var jawDirShort = new Vec2(sx * CornerJawSin, sy * CornerJawCos);
                var jawNormalShort = new Vec2(-sx * CornerJawCos, sy * CornerJawSin);
                cushions.Add(new CushionSegment(noseShort, noseShort + JawLength * jawDirShort, jawNormalShort, feature++));

                pocketId++;
            }
        }

        // Side pockets: at (0, ±HW), one jaw each side of the mouth.
        foreach (var sy in Signs)
        {
            var railY = sy * HalfWidth;
            pockets.Add(new Pocket(new Vec2(0.0, railY + sy * SideFallOffset), SideFallRadius, pocketId++));

            foreach (var sx in Signs)
            {
                var nose = new Vec2(sx * SideMouth / 2.0, railY);
                var jawDir = new Vec2(-sx * SideJawSin, sy * SideJawCos);
                var jawNormal = new Vec2(-sx * SideJawCos, -sy * SideJawSin);
                cushions.Add(new CushionSegment(nose, nose + JawLength * jawDir, jawNormal, feature++));
            }
        }

        return new TableSpec
        {
            Name = "Pool 9ft (WPA)",
            HalfLength = HalfLength,
            HalfWidth = HalfWidth,
            Physics = PhysicsParams.Pool(),
            Cushions = cushions,
            Jaws = new List<JawArc>(), // pool uses straight cut jaws; arcs arrive with the snooker table
            Pockets = pockets,
        };
    }
}
