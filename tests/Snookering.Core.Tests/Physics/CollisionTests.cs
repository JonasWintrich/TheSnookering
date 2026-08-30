using System;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Snookering.Core.Tables;
using Xunit;

namespace Snookering.Core.Tests.Physics;

public class CollisionTests
{
    private static readonly PhysicsParams P = PhysicsParams.Pool();

    [Fact]
    public void HeadOn_EqualMasses_SplitsByRestitution()
    {
        const double v = 2.0;
        var a = BallState.AtRest(0, Vec2.Zero);
        a.Vel = new Vec2(v, 0.0);
        a.State = MotionState.Sliding;
        var b = BallState.AtRest(1, new Vec2(2.0 * P.R, 0.0));

        Collisions.ResolveBallBall(ref a, ref b, P);

        var e = P.BallBallRestitution;
        Assert.Equal((1.0 - e) * v / 2.0, a.Vel.X, 12);
        Assert.Equal((1.0 + e) * v / 2.0, b.Vel.X, 12);
        Assert.Equal(0.0, a.Vel.Y, 12);
        Assert.Equal(0.0, b.Vel.Y, 12);
    }

    [Fact]
    public void BallBallToi_FindsExactContactTime()
    {
        var a = BallState.AtRest(0, Vec2.Zero);
        a.Vel = new Vec2(1.0, 0.0);
        var b = BallState.AtRest(1, new Vec2(1.0 + 2.0 * P.R, 0.0));

        var t = Collisions.BallBallToi(in a, in b, P.R, 10.0);
        Assert.Equal(1.0, t, 12);
    }

    [Fact]
    public void BallBallToi_IgnoresSeparatingBalls()
    {
        var a = BallState.AtRest(0, Vec2.Zero);
        a.Vel = new Vec2(-1.0, 0.0);
        var b = BallState.AtRest(1, new Vec2(3.0 * P.R, 0.0));

        Assert.True(double.IsPositiveInfinity(Collisions.BallBallToi(in a, in b, P.R, 10.0)));
    }

    [Fact]
    public void SpinlessCushion_ReversesNormalByRestitution_KeepsTangentialSign()
    {
        // 45° impact on a cushion whose inward normal is +Y.
        var ball = BallState.AtRest(0, Vec2.Zero);
        ball.Vel = new Vec2(1.0, -1.0);
        ball.State = MotionState.Sliding;

        Collisions.ResolveCushion(ref ball, new Vec2(0.0, 1.0), P);

        Assert.Equal(P.CushionRestitutionAt(1.0) * 1.0, ball.Vel.Y, 12);   // normal reversed & scaled
        Assert.True(ball.Vel.X > 0.0 && ball.Vel.X <= 1.0);               // tangential reduced by friction, sign kept
    }

    [Fact]
    public void CutShot_WithEnglish_ThrowsObjectBall()
    {
        // Straight full hit but cue ball carries heavy right english (ωz > 0):
        // friction at the contact throws the object ball off the center line.
        var cueNoSpin = BallState.AtRest(0, Vec2.Zero);
        cueNoSpin.Vel = new Vec2(2.0, 0.0);
        cueNoSpin.State = MotionState.Sliding;
        var obj1 = BallState.AtRest(1, new Vec2(2.0 * P.R, 0.0));
        Collisions.ResolveBallBall(ref cueNoSpin, ref obj1, P);

        var cueSpun = BallState.AtRest(0, Vec2.Zero);
        cueSpun.Vel = new Vec2(2.0, 0.0);
        cueSpun.AngVel = new Vec3(0.0, 0.0, 50.0);
        cueSpun.State = MotionState.Sliding;
        var obj2 = BallState.AtRest(1, new Vec2(2.0 * P.R, 0.0));
        Collisions.ResolveBallBall(ref cueSpun, ref obj2, P);

        Assert.Equal(0.0, obj1.Vel.Y, 12);
        Assert.NotEqual(0.0, obj2.Vel.Y);
        // English transfer: the object ball must pick up some vertical spin.
        Assert.NotEqual(0.0, obj2.AngVel.Z);
    }

    [Fact]
    public void Cushion_RunningEnglish_ChangesReboundAngle()
    {
        Vec2 Rebound(double wz)
        {
            var ball = BallState.AtRest(0, Vec2.Zero);
            ball.Vel = new Vec2(1.0, -1.0);
            ball.AngVel = new Vec3(0.0, 0.0, wz);
            ball.State = MotionState.Sliding;
            Collisions.ResolveCushion(ref ball, new Vec2(0.0, 1.0), P);
            return ball.Vel;
        }

        var neutral = Rebound(0.0);
        var running = Rebound(-40.0); // spin whose surface motion at contact adds to +X slip? — must differ
        var check = Rebound(40.0);

        Assert.NotEqual(neutral.X, running.X);
        Assert.NotEqual(neutral.X, check.X);
        // Running and check english must push the tangential exit speed in opposite directions.
        Assert.True(Math.Sign(running.X - neutral.X) == -Math.Sign(check.X - neutral.X));
    }

    [Fact]
    public void Cushion_TopspinBleed_NormalImpulseTorquesBall()
    {
        // Straight-on impact with pure topspin: the elevated contact point converts
        // some topspin, and friction acts against the contact slip.
        var ball = BallState.AtRest(0, Vec2.Zero);
        ball.Vel = new Vec2(0.0, -2.0);
        ball.AngVel = new Vec3(-ball.Vel.Y / P.R, 0.0, 0.0); // natural-roll topspin (ωx = −vy/R)
        ball.State = MotionState.Rolling;

        var before = ball.AngVel.X;
        Collisions.ResolveCushion(ref ball, new Vec2(0.0, 1.0), P);

        Assert.True(Math.Abs(ball.AngVel.X) < Math.Abs(before), "cushion must bleed topspin");
        Assert.Equal(2.0 * P.CushionRestitutionAt(2.0), ball.Vel.Y, 12);
    }

    [Fact]
    public void Topspin_TransfersToObjectBall_LikeMeshingGears()
    {
        // Full straight hit with a rolling (topspin) cue ball. Contact surfaces move
        // like meshing gears, so the object ball must pick up the OPPOSITE sense of
        // spin while the cue ball loses some of its own.
        const double v = 2.0;
        var cue = BallState.AtRest(0, Vec2.Zero);
        cue.Vel = new Vec2(v, 0.0);
        cue.AngVel = new Vec3(0.0, v / P.R, 0.0);   // natural roll for +X travel
        cue.State = MotionState.Rolling;
        var obj = BallState.AtRest(1, new Vec2(2.0 * P.R, 0.0));

        var spinBefore = cue.AngVel.Y;
        Collisions.ResolveBallBall(ref cue, ref obj, P);

        Assert.True(obj.AngVel.Y < 0.0, "object ball must pick up reverse spin from the cue ball's topspin");
        Assert.True(cue.AngVel.Y < spinBefore, "cue ball must give some topspin away");
        // The gear coupling is equal and opposite about the same axis.
        Assert.Equal(spinBefore - cue.AngVel.Y, -obj.AngVel.Y, 9);
    }

    [Fact]
    public void SpinlessFullHit_TransfersNoSpin()
    {
        // Guard the above: with no spin anywhere there is nothing to transfer.
        var cue = BallState.AtRest(0, Vec2.Zero);
        cue.Vel = new Vec2(2.0, 0.0);
        cue.State = MotionState.Sliding;
        var obj = BallState.AtRest(1, new Vec2(2.0 * P.R, 0.0));

        Collisions.ResolveBallBall(ref cue, ref obj, P);

        Assert.Equal(Vec3.Zero, obj.AngVel);
        Assert.Equal(Vec3.Zero, cue.AngVel);
    }

    [Fact]
    public void CushionRestitution_FallsWithImpactSpeed()
    {
        double ReboundRatio(double speed)
        {
            var ball = BallState.AtRest(0, Vec2.Zero);
            ball.Vel = new Vec2(0.0, -speed);
            ball.State = MotionState.Sliding;
            Collisions.ResolveCushion(ref ball, new Vec2(0.0, 1.0), P);
            return ball.Vel.Y / speed;
        }

        var soft = ReboundRatio(0.5);
        var hard = ReboundRatio(6.0);

        Assert.True(soft > hard, $"a gentle roll must come back livelier than a power drive ({soft:F3} vs {hard:F3})");
        Assert.Equal(P.CushionRestitutionAt(0.5), soft, 9);
        Assert.Equal(P.CushionRestitutionAt(6.0), hard, 9);
        // And the curve stays inside its stated bounds at both extremes.
        Assert.InRange(P.CushionRestitutionAt(0.0), P.CushionRestitutionMin, P.CushionRestitutionBase);
        Assert.InRange(P.CushionRestitutionAt(50.0), P.CushionRestitutionMin, P.CushionRestitutionBase);
    }

    [Fact]
    public void SegmentToi_FaceAndNose()
    {
        var seg = new CushionSegment(new Vec2(-1.0, 1.0), new Vec2(1.0, 1.0), new Vec2(0.0, -1.0), 0);

        var face = BallState.AtRest(0, Vec2.Zero);
        face.Vel = new Vec2(0.0, 1.0);
        var tFace = Collisions.BallSegmentToi(in face, seg, P.R, 10.0, out var nFace);
        Assert.Equal(1.0 - P.R, tFace, 12);
        Assert.Equal(new Vec2(0.0, -1.0), nFace);

        // Aimed past the end of the face: hits the nose point at A instead.
        var nose = BallState.AtRest(0, new Vec2(-1.5, 1.0));
        nose.Vel = new Vec2(1.0, 0.0);
        var tNose = Collisions.BallSegmentToi(in nose, seg, P.R, 10.0, out var nNose);
        Assert.Equal(0.5 - P.R, tNose, 12);
        Assert.Equal(-1.0, nNose.X, 12);
    }
}
