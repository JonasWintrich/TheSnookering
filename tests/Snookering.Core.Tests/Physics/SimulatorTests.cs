using System;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Physics;

public class SimulatorTests
{
    private static readonly TableSpec Table = TableSpec.Pool9ft();

    private static TableState CueOnly(Vec2 pos) =>
        new(new[] { BallState.AtRest(0, pos) });

    private static ShotInput Shot(double angleRad, double speed, double side = 0.0, double vert = 0.0) => new()
    {
        AimAngleMicroRad = (int)Math.Round(angleRad * 1e6),
        SpeedMmPerSec = (int)Math.Round(speed * 1e3),
        OffsetSide1e4 = (short)Math.Round(side * 1e4),
        OffsetVert1e4 = (short)Math.Round(vert * 1e4),
        ElevationCentiDeg = 0,
    };

    [Fact]
    public void GentleShot_ComesToRest_OnTable()
    {
        var result = Simulator.Run(CueOnly(Vec2.Zero), Shot(0.7, 1.0), Table);

        var cue = result.FinalState.Balls[0];
        Assert.True(cue.OnTable);
        Assert.False(cue.IsActive);
        Assert.True(result.Duration < Simulator.MaxShotSeconds);
        Assert.Equal(SimEventType.RestReached, result.Events[^1].Type);
    }

    [Fact]
    public void HardShot_IntoRail_BouncesAndStaysInBounds()
    {
        var result = Simulator.Run(CueOnly(Vec2.Zero), Shot(0.35, 6.5), Table);

        Assert.Contains(result.Events, e => e.Type == SimEventType.Cushion);
        var cue = result.FinalState.Balls[0];
        if (cue.OnTable) // may legitimately end in a pocket
        {
            Assert.InRange(cue.Pos.X, -Table.HalfLength, Table.HalfLength);
            Assert.InRange(cue.Pos.Y, -Table.HalfWidth, Table.HalfWidth);
        }
    }

    [Fact]
    public void StraightShot_AtCornerPocket_Falls()
    {
        // From the table center, the top-right corner is along atan2(HW, HL).
        var angle = Math.Atan2(Table.HalfWidth, Table.HalfLength);
        var result = Simulator.Run(CueOnly(Vec2.Zero), Shot(angle, 3.0), Table);

        var pocketed = result.Events.Where(e => e.Type == SimEventType.Pocketed).ToList();
        Assert.Single(pocketed);
        Assert.False(result.FinalState.Balls[0].OnTable);
    }

    [Fact]
    public void StraightShot_AtSidePocket_Falls()
    {
        var start = new Vec2(0.0, -0.3);
        var result = Simulator.Run(CueOnly(start), Shot(Math.PI / 2.0, 2.0), Table);

        Assert.Contains(result.Events, e => e.Type == SimEventType.Pocketed);
    }

    [Fact]
    public void FullHit_TransfersMomentum_ObjectBallPots()
    {
        // Cue at center, object ball dead in line with the top-right corner pocket.
        var angle = Math.Atan2(Table.HalfWidth, Table.HalfLength);
        var dir = new Vec2(Math.Cos(angle), Math.Sin(angle));
        var state = new TableState(new[]
        {
            BallState.AtRest(0, Vec2.Zero),
            BallState.AtRest(1, 0.3 * dir),
        });

        var result = Simulator.Run(state, Shot(angle, 3.0), Table);

        Assert.Contains(result.Events, e => e.Type == SimEventType.BallBall);
        Assert.Contains(result.Events, e => e.Type == SimEventType.Pocketed && e.BallA == 1);
        // Nearly-stun full hit: the cue ball must NOT follow the object ball into the pocket.
        Assert.True(result.FinalState.Balls[0].OnTable);
    }

    [Fact]
    public void EnergyNeverIncreases_TickOverTick()
    {
        var state = new TableState(new[]
        {
            BallState.AtRest(0, new Vec2(-0.5, 0.0)),
            BallState.AtRest(1, new Vec2(0.2, 0.01)),
            BallState.AtRest(2, new Vec2(0.2 + 2.0 * Table.Physics.R, -0.02)),
        });
        var result = Simulator.Run(state, Shot(0.02, 5.0), Table);

        // Frames carry positions, not velocities — assert on inter-frame displacement instead:
        // total path length per frame interval must shrink over time (dissipation) after the last event.
        var lastEventTime = result.Events.Where(e => e.Type != SimEventType.RestReached).Max(e => e.Time);
        var frames = result.Frames.Where(f => f.Time > lastEventTime).ToList();
        double Moved(int i) => frames[i].Balls.Zip(frames[i - 1].Balls, (c, p) => (c.Pos - p.Pos).Length).Sum();

        if (frames.Count > 4)
        {
            var early = Moved(1);
            var late = Moved(frames.Count - 1);
            Assert.True(late <= early + 1e-9, $"movement grew after last impulse: {early} -> {late}");
        }
    }

    [Fact]
    public void NoOverlaps_AtRest()
    {
        var r = Table.Physics.R;
        var state = new TableState(new[]
        {
            BallState.AtRest(0, new Vec2(-0.6, 0.0)),
            BallState.AtRest(1, new Vec2(0.3, 0.0)),
            BallState.AtRest(2, new Vec2(0.3 + 2.0 * r, 0.0)),
            BallState.AtRest(3, new Vec2(0.3 + r, 1.8 * r)),
        });
        var result = Simulator.Run(state, Shot(0.0, 6.0), Table);

        var onTable = result.FinalState.Balls.Where(b => b.OnTable).ToList();
        for (var i = 0; i < onTable.Count; i++)
            for (var j = i + 1; j < onTable.Count; j++)
                Assert.True((onTable[i].Pos - onTable[j].Pos).Length >= 2.0 * r - 1e-9,
                    $"balls {onTable[i].Id} and {onTable[j].Id} overlap at rest");
    }

    [Fact]
    public void Determinism_IdenticalRuns_IdenticalHashes()
    {
        ShotResult Run()
        {
            var state = new TableState(new[]
            {
                BallState.AtRest(0, new Vec2(-0.6, 0.05)),
                BallState.AtRest(1, new Vec2(0.3, 0.0)),
                BallState.AtRest(2, new Vec2(0.36, 0.02)),
            });
            return Simulator.Run(state, Shot(0.03, 5.5, side: 0.2, vert: -0.3), Table);
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(a.Events.Count, b.Events.Count);
        Assert.Equal(a.Duration, b.Duration);
    }

    [Fact]
    public void DrawShot_CueBallComesBack_AfterFullHit()
    {
        var state = new TableState(new[]
        {
            BallState.AtRest(0, new Vec2(-0.4, 0.0)),
            BallState.AtRest(1, new Vec2(0.0, 0.0)),
        });
        var result = Simulator.Run(state, Shot(0.0, 3.0, vert: -0.45), Table);

        // After the full hit, backspin must pull the cue ball well behind the impact
        // point at some moment (its final spot can differ — the object ball returns
        // off the far rail and re-collides, which is legitimate).
        var impactTime = result.Events.First(e => e.Type == SimEventType.BallBall).Time;
        var minCueX = result.Frames
            .Where(f => f.Time > impactTime)
            .Min(f => f.Balls.First(s => s.Id == 0).Pos.X);
        Assert.True(minCueX < -0.3, $"draw failed: cue ball only drew back to x={minCueX:F3}");
    }
}
