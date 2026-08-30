using System;
using System.Collections.Generic;
using System.Linq;
using Snookering.Core.Ai;
using Snookering.Core.Physics;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Physics;

/// <summary>
/// The player watches the trajectory frames, not the final state, so a ball that
/// crosses a cushion mid-shot and comes back still looks like it went through
/// the wood. Every sampled frame must keep every ball inside the cushions.
/// </summary>
public class FrameContainmentTests
{
    private static ShotInput Shot(double angle, double speed, double side, double vert) => new()
    {
        AimAngleMicroRad = (int)Math.Round(angle * 1e6),
        SpeedMmPerSec = (int)Math.Round(speed * 1e3),
        OffsetSide1e4 = (short)Math.Round(side * 1e4),
        OffsetVert1e4 = (short)Math.Round(vert * 1e4),
        ElevationCentiDeg = 0,
    };

    private static List<string> Excursions(TableSpec table, bool snooker, int shots)
    {
        var rng = new DeterministicRng(20260824);
        var bad = new List<string>();
        var r = table.Physics.R;
        // A ball resting against a cushion has its centre one radius inside, so
        // anything past the rail line is already through the cloth face.
        var limitX = table.HalfLength + r * 0.35;
        var limitY = table.HalfWidth + r * 0.35;

        for (var s = 0; s < shots; s++)
        {
            var state = snooker ? Racks.Snooker(table) : Racks.EightBall(table);
            var angle = rng.NextDouble() * Math.Tau;
            var speed = 1.0 + rng.NextDouble() * 6.0;
            var side = (rng.NextDouble() * 2.0 - 1.0) * 0.45;
            var vert = (rng.NextDouble() * 2.0 - 1.0) * 0.45;

            var result = Simulator.Run(state, Shot(angle, speed, side, vert), table);

            foreach (var frame in result.Frames)
            {
                foreach (var b in frame.Balls)
                {
                    if (!b.OnTable)
                        continue;
                    // A ball entering a pocket legitimately has its centre past the
                    // rail line — that is what a pocket mouth is. Only flag balls
                    // that are through the cloth somewhere with a cushion behind it.
                    if (table.Pockets.Any(pk => (pk.FallCenter - b.Pos).Length < pk.FallRadius + 2.5 * r))
                        continue;
                    if (Math.Abs(b.Pos.X) > limitX || Math.Abs(b.Pos.Y) > limitY)
                    {
                        bad.Add($"shot {s}: ball {b.Id} at t={frame.Time:F2}s is at {b.Pos} " +
                                $"(limits {limitX:F3}/{limitY:F3})");
                        break;
                    }
                }
                if (bad.Count > 0 && bad[^1].StartsWith($"shot {s}:"))
                    break;
            }
        }
        return bad;
    }

    [Fact]
    public void PoolBreaks_NeverShowABallThroughACushion()
    {
        var bad = Excursions(TableSpec.Pool9ft(), snooker: false, shots: 60);
        Assert.True(bad.Count == 0,
            $"{bad.Count} shots showed a ball outside the cushions:\n  " + string.Join("\n  ", bad.Take(8)));
    }

    [Fact]
    public void SnookerBreaks_NeverShowABallThroughACushion()
    {
        var bad = Excursions(TableSpec.Snooker12ft(), snooker: true, shots: 60);
        Assert.True(bad.Count == 0,
            $"{bad.Count} shots showed a ball outside the cushions:\n  " + string.Join("\n  ", bad.Take(8)));
    }
}
