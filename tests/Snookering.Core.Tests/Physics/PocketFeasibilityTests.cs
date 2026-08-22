using System;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Physics;

/// <summary>
/// Guards pocket geometry tuning: on BOTH tables a clean straight shot at each
/// pocket type must drop, and on snooker a clearly off-line corner shot must
/// rattle out (tight pockets punish, they don't seal).
/// </summary>
public class PocketFeasibilityTests
{
    private static ShotInput Aim(Vec2 from, Vec2 to, double speed) => new()
    {
        AimAngleMicroRad = (int)Math.Round(Math.Atan2(to.Y - from.Y, to.X - from.X) * 1e6),
        SpeedMmPerSec = (int)Math.Round(speed * 1e3),
        OffsetSide1e4 = 0,
        OffsetVert1e4 = 0,
        ElevationCentiDeg = 0,
    };

    private static bool Pots(TableSpec table, Vec2 from, Vec2 target, double speed)
    {
        var state = new TableState(new[] { BallState.AtRest(0, from) });
        var result = Simulator.Run(state, Aim(from, target, speed), table);
        return result.Events.Any(e => e.Type == SimEventType.Pocketed);
    }

    public static TheoryData<string> Tables() => new() { "pool", "snooker" };

    [Theory]
    [MemberData(nameof(Tables))]
    public void StraightShot_IntoEveryPocket_Drops(string which)
    {
        var table = which == "pool" ? TableSpec.Pool9ft() : TableSpec.Snooker12ft();

        foreach (var pocket in table.Pockets)
        {
            // Approach along the pocket's own centerline: the 45° diagonal for
            // corners, straight-on for the side pockets. Aiming from anywhere else
            // toward the fall center clips a rail first (a genuinely bad shot).
            var isSide = Math.Abs(pocket.FallCenter.X) < 0.2;
            var axis = isSide
                ? new Vec2(0.0, Math.Sign(pocket.FallCenter.Y))
                : new Vec2(
                    Math.Sign(pocket.FallCenter.X) * 0.7071067811865476,
                    Math.Sign(pocket.FallCenter.Y) * 0.7071067811865476);
            var from = pocket.FallCenter - axis * (isSide ? 0.6 : 1.0);

            Assert.True(Pots(table, from, pocket.FallCenter, 2.5),
                $"{table.Name}: straight shot into pocket {pocket.Id} at {pocket.FallCenter} did not drop");
        }
    }

    [Fact]
    public void Snooker_CornerShot_WellOffLine_RattlesOut()
    {
        var table = TableSpec.Snooker12ft();
        var corner = table.Pockets[0].FallCenter;

        // Aim 30 mm to the side of the fall center from 1 m away — must NOT drop.
        var from = corner - new Vec2(1.0, 0.4).Normalized() * 1.0;
        var offTarget = corner + new Vec2(-0.03, 0.03);
        var state = new TableState(new[] { BallState.AtRest(0, from) });
        var result = Simulator.Run(state, Aim(from, offTarget, 2.0), table);

        Assert.DoesNotContain(result.Events, e => e.Type == SimEventType.Pocketed);
        Assert.Contains(result.Events, e => e.Type == SimEventType.Cushion); // it rattled the jaws
    }

    [Fact]
    public void Snooker_CornerShot_SlightlyOffLine_StillDrops()
    {
        // ~7 mm off the centerline from a meter out (down the pocket diagonal)
        // must still pot — pockets are tight, not sealed.
        var table = TableSpec.Snooker12ft();
        var corner = table.Pockets[0].FallCenter;
        var diag = new Vec2(Math.Sign(corner.X) * 0.7071067811865476, Math.Sign(corner.Y) * 0.7071067811865476);
        var from = corner - diag * 1.0;
        var slightlyOff = corner + diag.Perp * 0.007;

        Assert.True(Pots(table, from, slightlyOff, 2.2), "7 mm-off corner shot should still drop");
    }
}
