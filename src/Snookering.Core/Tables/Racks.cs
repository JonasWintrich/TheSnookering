using Snookering.Core.Mathematics;
using Snookering.Core.Physics;

namespace Snookering.Core.Tables;

/// <summary>
/// Standard rack layouts. Ball ids: 0 = cue, 1–7 solids, 8 = eight ball, 9–15 stripes.
/// Deterministic arrangement satisfying the 8-ball racking rules: apex on the foot
/// spot, 8 in the center of the third row, opposite groups in the back corners.
/// </summary>
public static class Racks
{
    private const double Sqrt3Over2 = 0.8660254037844386;

    /// <summary>Tiny spacing factor so racked balls are not in exact contact (avoids a TOI storm on the break).</summary>
    private const double RackSpacing = 1.0005;

    private static readonly byte[][] EightBallRows =
    {
        new byte[] { 1 },
        new byte[] { 9, 2 },
        new byte[] { 3, 8, 10 },
        new byte[] { 11, 4, 12, 5 },
        new byte[] { 6, 13, 7, 14, 15 },
    };

    /// <summary>Foot spot: center of the racking half (+X), pool convention: quarter table.</summary>
    public static Vec2 FootSpot(TableSpec table) => new(table.HalfLength / 2.0, 0.0);

    /// <summary>Head spot: cue-ball break position mirror (−X half).</summary>
    public static Vec2 HeadSpot(TableSpec table) => new(-table.HalfLength / 2.0, 0.0);

    /// <summary>Snooker frame: 15 reds behind the pink, colors on their spots, cue in the D.</summary>
    public static TableState Snooker(TableSpec table)
    {
        var spots = table.Snooker!;
        var r = table.Physics.R;
        var step = 2.0 * r * RackSpacing;

        var balls = new BallState[22];
        balls[0] = BallState.AtRest(0, new Vec2(spots.BaulkX - 0.16, 0.12)); // in the D

        var apex = new Vec2(spots.Pink.X + 2.0 * r + 0.004, 0.0);
        byte red = 1;
        var index = 1;
        for (var row = 0; row < 5; row++)
        {
            var x = apex.X + row * step * Sqrt3Over2;
            var y0 = -0.5 * row * step;
            for (var k = 0; k <= row; k++)
                balls[index++] = BallState.AtRest(red++, new Vec2(x, y0 + k * step));
        }

        balls[index++] = BallState.AtRest(SnookerBalls.Yellow, spots.Yellow);
        balls[index++] = BallState.AtRest(SnookerBalls.Green, spots.Green);
        balls[index++] = BallState.AtRest(SnookerBalls.Brown, spots.Brown);
        balls[index++] = BallState.AtRest(SnookerBalls.Blue, spots.Blue);
        balls[index++] = BallState.AtRest(SnookerBalls.Pink, spots.Pink);
        balls[index] = BallState.AtRest(SnookerBalls.Black, spots.Black);

        return new TableState(balls);
    }

    public static TableState EightBall(TableSpec table)
    {
        var r = table.Physics.R;
        var step = 2.0 * r * RackSpacing;
        var foot = FootSpot(table);

        var balls = new BallState[16];
        balls[0] = BallState.AtRest(0, HeadSpot(table));

        var index = 1;
        for (var row = 0; row < EightBallRows.Length; row++)
        {
            var ids = EightBallRows[row];
            var x = foot.X + row * step * Sqrt3Over2;
            var y0 = -0.5 * (ids.Length - 1) * step;
            for (var k = 0; k < ids.Length; k++)
                balls[index++] = BallState.AtRest(ids[k], new Vec2(x, y0 + k * step));
        }

        return new TableState(balls);
    }
}
