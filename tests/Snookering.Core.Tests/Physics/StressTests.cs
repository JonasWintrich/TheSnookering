using System;
using System.Diagnostics;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Physics;

/// <summary>
/// Every shot must terminate quickly, no matter how awkward — balls wedged against
/// cushions, jaw rattles, crawling speeds. Guards against zero-progress event loops
/// that would freeze the game (sync or async).
/// </summary>
public class StressTests
{
    private static readonly TableSpec Table = TableSpec.Pool9ft();

    private static ShotInput Shot(double angleRad, double speed, double side, double vert) => new()
    {
        AimAngleMicroRad = (int)Math.Round(angleRad * 1e6),
        SpeedMmPerSec = (int)Math.Round(speed * 1e3),
        OffsetSide1e4 = (short)Math.Round(side * 1e4),
        OffsetVert1e4 = (short)Math.Round(vert * 1e4),
        ElevationCentiDeg = 0,
    };

    [Fact]
    public void AwkwardShots_AllTerminate_WithinTimeBudget()
    {
        var r = Table.Physics.R;
        var sw = Stopwatch.StartNew();
        var shots = 0;

        // Cue against each rail, shooting along and into the rail at varied speeds/spins.
        var starts = new[]
        {
            new Vec2(0.0, Table.HalfWidth - r),             // touching top rail
            new Vec2(0.0, -(Table.HalfWidth - r)),          // touching bottom rail
            new Vec2(Table.HalfLength - r, 0.0),            // touching right rail
            new Vec2(Table.HalfLength - 0.09, Table.HalfWidth - 0.09), // in the corner jaw area
            new Vec2(0.02, Table.HalfWidth - r),            // beside the side pocket mouth
        };
        var angles = new[] { 0.0, Math.PI / 2.0, Math.PI, -Math.PI / 2.0, 0.6, 2.3 };
        var speeds = new[] { 0.4, 2.0, 7.0 };
        var spins = new[] { (0.0, 0.0), (0.45, 0.0), (0.0, -0.45), (-0.3, 0.3) };

        foreach (var start in starts)
        foreach (var angle in angles)
        foreach (var speed in speeds)
        {
            var (side, vert) = spins[shots % spins.Length];
            var state = new TableState(new[]
            {
                BallState.AtRest(0, start),
                BallState.AtRest(1, new Vec2(-0.4, 0.2)),
            });

            var result = Simulator.Run(state, Shot(angle, speed, side, vert), Table);
            shots++;

            Assert.True(result.Duration < Simulator.MaxShotSeconds,
                $"shot {shots} (start {start}, angle {angle:F2}, v {speed}) hit the time cap");
        }

        sw.Stop();
        Assert.True(sw.Elapsed.TotalSeconds < 10.0,
            $"{shots} shots took {sw.Elapsed.TotalSeconds:F1}s — simulator too slow (zero-progress loop?)");
    }

    [Fact]
    public void FullBreak_SimulatesFast()
    {
        var state = Racks.EightBall(Table);
        var sw = Stopwatch.StartNew();
        var result = Simulator.Run(state, Shot(0.0, 7.0, 0.0, 0.0), Table);
        sw.Stop();

        Assert.True(result.Duration < Simulator.MaxShotSeconds, "break hit the sim time cap");
        Assert.True(sw.Elapsed.TotalMilliseconds < 2000.0,
            $"break took {sw.Elapsed.TotalMilliseconds:F0}ms to simulate");
    }
}
