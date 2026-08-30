using System;
using System.Collections.Generic;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Physics;

/// <summary>
/// A ball must always end a shot either on the cloth or in a pocket — never
/// outside the cushions. Escapes are the worst class of physics bug because the
/// rules engine then adjudicates a table state that cannot happen.
/// </summary>
public class ContainmentTests
{
    private static ShotInput Aim(double angleRad, double speed) => new()
    {
        AimAngleMicroRad = (int)Math.Round(angleRad * 1e6),
        SpeedMmPerSec = (int)Math.Round(speed * 1e3),
        OffsetSide1e4 = 0,
        OffsetVert1e4 = 0,
        ElevationCentiDeg = 0,
    };

    private static List<string> Escapes(TableSpec table, int angleSteps, double[] speeds, Vec2[] starts)
    {
        var escapes = new List<string>();
        var r = table.Physics.R;

        foreach (var start in starts)
        foreach (var speed in speeds)
        for (var a = 0; a < angleSteps; a++)
        {
            var angle = Math.Tau * a / angleSteps;
            var state = new TableState(new[] { BallState.AtRest(0, start) });
            var result = Simulator.Run(state, Aim(angle, speed), table);

            var ball = result.FinalState.Balls[0];
            if (!ball.OnTable)
                continue; // pocketed is a legal outcome

            var outX = Math.Abs(ball.Pos.X) > table.HalfLength + r;
            var outY = Math.Abs(ball.Pos.Y) > table.HalfWidth + r;
            if (outX || outY)
                escapes.Add($"from {start} at {angle * 180 / Math.PI:F0}° {speed:F1} m/s → {ball.Pos}");
        }
        return escapes;
    }

    [Fact]
    public void PoolTable_ContainsEveryShot()
    {
        var table = TableSpec.Pool9ft();
        var starts = new[]
        {
            Vec2.Zero,
            new Vec2(0.0, 0.35),                       // straight at a side pocket
            new Vec2(-0.9, 0.0),
            new Vec2(0.6, -0.3),
            new Vec2(1.0, 0.45),                       // near a corner
            new Vec2(0.05, 0.5),                       // just off the side pocket mouth
        };
        var escapes = Escapes(table, 72, new[] { 1.5, 4.0, 7.0 }, starts);
        Assert.True(escapes.Count == 0,
            $"{escapes.Count} balls escaped the pool table:\n  " + string.Join("\n  ", escapes.Take(12)));
    }

    [Fact]
    public void SnookerTable_ContainsEveryShot()
    {
        var table = TableSpec.Snooker12ft();
        var starts = new[]
        {
            Vec2.Zero,
            new Vec2(0.0, 0.5),
            new Vec2(-1.2, 0.0),
            new Vec2(1.4, 0.6),
            new Vec2(0.05, 0.75),
        };
        var escapes = Escapes(table, 72, new[] { 1.5, 4.0, 7.0 }, starts);
        Assert.True(escapes.Count == 0,
            $"{escapes.Count} balls escaped the snooker table:\n  " + string.Join("\n  ", escapes.Take(12)));
    }
}
