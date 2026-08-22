using System;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Xunit;

namespace Snookering.Core.Tests.Physics;

public class BallMotionTests
{
    private const double Dt = 1.0 / 240.0;
    private static readonly PhysicsParams P = PhysicsParams.Pool();

    private static BallState StunShot(double v0)
    {
        var b = BallState.AtRest(0, Vec2.Zero);
        b.Vel = new Vec2(v0, 0.0);
        b.State = MotionState.Sliding;
        return b;
    }

    private static void RunToRest(ref BallState b)
    {
        var guard = 0;
        while (b.IsActive)
        {
            BallMotion.Integrate(ref b, P, Dt);
            Assert.True(++guard < 1_000_000, "ball never came to rest");
        }
    }

    [Fact]
    public void StunShot_TransitionsToRolling_AtFiveSeventhsSpeed()
    {
        const double v0 = 2.0;
        var b = StunShot(v0);

        // Closed form: slip exhausts at t* = 2v0/(7 μs g); integrate exactly that long.
        var slideTime = 2.0 * v0 / (7.0 * P.MuSlide * P.G);
        BallMotion.Integrate(ref b, P, slideTime);

        Assert.Equal(MotionState.Rolling, b.State);
        Assert.Equal(5.0 * v0 / 7.0, b.Vel.X, 9);
        Assert.Equal(0.0, b.Vel.Y, 12);
        // Rolling constraint: ωy = vx / R.
        Assert.Equal(b.Vel.X / P.R, b.AngVel.Y, 9);
        // Slide distance: 12 v0² / (49 μs g).
        Assert.Equal(12.0 * v0 * v0 / (49.0 * P.MuSlide * P.G), b.Pos.X, 9);
    }

    [Fact]
    public void StunShot_TotalDistance_MatchesClosedForm()
    {
        const double v0 = 2.0;
        var b = StunShot(v0);
        RunToRest(ref b);

        var slideDist = 12.0 * v0 * v0 / (49.0 * P.MuSlide * P.G);
        var vRoll = 5.0 * v0 / 7.0;
        var rollDist = vRoll * vRoll / (2.0 * P.MuRoll * P.G);

        Assert.Equal(slideDist + rollDist, b.Pos.X, 6);
        Assert.Equal(0.0, b.Pos.Y, 12);
        Assert.Equal(MotionState.Stationary, b.State);
        Assert.Equal(Vec3.Zero, b.AngVel);
    }

    [Fact]
    public void RollingBall_StopDistance_IsVSquaredOver2MuG()
    {
        const double v = 1.5;
        var b = BallState.AtRest(0, Vec2.Zero);
        b.Vel = new Vec2(0.0, v);
        b.AngVel = new Vec3(-v / P.R, 0.0, 0.0);
        b.State = MotionState.Rolling;

        RunToRest(ref b);

        Assert.Equal(v * v / (2.0 * P.MuRoll * P.G), b.Pos.Y, 9);
        Assert.Equal(0.0, b.Pos.X, 12);
    }

    [Fact]
    public void VerticalSpin_DecaysLinearly_AndStopsExactly()
    {
        const double wz = 30.0;
        var b = BallState.AtRest(0, Vec2.Zero);
        b.AngVel = new Vec3(0.0, 0.0, wz);

        var rate = 2.5 * P.MuSpin * P.G / P.R;
        var half = wz / (2.0 * rate);
        BallMotion.Integrate(ref b, P, half);
        Assert.Equal(wz / 2.0, b.AngVel.Z, 9);

        BallMotion.Integrate(ref b, P, half + 1.0);
        Assert.Equal(0.0, b.AngVel.Z);
        Assert.False(b.IsActive);
        Assert.Equal(Vec2.Zero, b.Pos);
    }

    [Fact]
    public void DrawShot_BackspinDelaysRoll_AndSlidesLongerThanStun()
    {
        const double v0 = 2.0;
        var stun = StunShot(v0);
        var draw = StunShot(v0);
        draw.AngVel = new Vec3(0.0, -0.5 * v0 / P.R, 0.0); // backspin

        // Slip for draw = v0 + R·|ωy| = 1.5·v0 ⇒ slide phase lasts 1.5× longer.
        // Compare AFTER the stun ball starts rolling (μr·g ≪ μs·g) but before the
        // draw ball does: 1.25× the stun slide time sits strictly between the two.
        var stunSlide = 2.0 * v0 / (7.0 * P.MuSlide * P.G);
        BallMotion.Integrate(ref stun, P, 1.25 * stunSlide);
        BallMotion.Integrate(ref draw, P, 1.25 * stunSlide);

        Assert.Equal(MotionState.Rolling, stun.State);
        Assert.Equal(MotionState.Sliding, draw.State);
        Assert.True(draw.Vel.X < stun.Vel.X, "backspin must decelerate the ball harder");
    }

    [Fact]
    public void Integration_IsDeterministic_BitwiseIdenticalRuns()
    {
        BallState Run()
        {
            var b = StunShot(3.3);
            b.AngVel = new Vec3(4.0, -20.0, 15.0);
            for (var i = 0; i < 5000; i++)
                BallMotion.Integrate(ref b, P, Dt);
            return b;
        }

        var a = Run();
        var c = Run();
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Pos.X), BitConverter.DoubleToInt64Bits(c.Pos.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(a.Pos.Y), BitConverter.DoubleToInt64Bits(c.Pos.Y));
        Assert.Equal(a.Vel, c.Vel);
        Assert.Equal(a.AngVel, c.AngVel);
        Assert.Equal(a.State, c.State);
    }
}
