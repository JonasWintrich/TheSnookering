using System;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;
using Xunit;

namespace Snookering.Core.Tests.Physics;

public class CueStrikeTests
{
    private static readonly PhysicsParams P = PhysicsParams.Pool();

    private static ShotInput Shot(
        double angleRad = 0.0, double speed = 2.0,
        double side = 0.0, double vert = 0.0, double elevationDeg = 0.0) => new()
    {
        AimAngleMicroRad = (int)Math.Round(angleRad * 1e6),
        SpeedMmPerSec = (int)Math.Round(speed * 1e3),
        OffsetSide1e4 = (short)Math.Round(side * 1e4),
        OffsetVert1e4 = (short)Math.Round(vert * 1e4),
        ElevationCentiDeg = (short)Math.Round(elevationDeg * 100),
    };

    private static BallState Struck(ShotInput shot, bool squirt = false)
    {
        var b = BallState.AtRest(0, Vec2.Zero);
        CueStrike.Apply(ref b, shot, P, squirt);
        return b;
    }

    [Fact]
    public void CenterHit_PureVelocity_NoSpin()
    {
        var b = Struck(Shot(speed: 3.0));

        Assert.Equal(3.0, b.Vel.X, 12);
        Assert.Equal(0.0, b.Vel.Y, 12);
        Assert.Equal(Vec3.Zero, b.AngVel);
        Assert.Equal(MotionState.Sliding, b.State);
    }

    [Fact]
    public void FollowShot_TopspinAboutLeftAxis()
    {
        const double v = 2.0, offset = 0.4;
        var b = Struck(Shot(speed: v, vert: offset));

        // Aim +X, left axis = +Y: topspin means ωy > 0, magnitude 5·V·b/(2R).
        Assert.Equal(2.5 * v * offset / P.R, b.AngVel.Y, 9);
        Assert.Equal(0.0, b.AngVel.X, 12);
        Assert.Equal(0.0, b.AngVel.Z, 12);
    }

    [Fact]
    public void DrawShot_BackspinIsNegative()
    {
        var b = Struck(Shot(vert: -0.4));
        Assert.True(b.AngVel.Y < 0.0);
    }

    [Fact]
    public void LeftEnglish_GivesClockwiseSpinFromAbove()
    {
        const double v = 2.0, offset = 0.3;
        var b = Struck(Shot(speed: v, side: offset));

        Assert.Equal(-2.5 * v * offset / P.R, b.AngVel.Z, 9);
        Assert.Equal(0.0, b.AngVel.X, 12);
        Assert.Equal(0.0, b.AngVel.Y, 12);
    }

    [Fact]
    public void Elevation_ProjectsVelocity_AndAddsSwerveComponent()
    {
        const double v = 2.0, side = 0.3, elevDeg = 15.0;
        var b = Struck(Shot(speed: v, side: side, elevationDeg: elevDeg));
        var elev = elevDeg * Math.PI / 180.0;

        Assert.Equal(v * Math.Cos(elev), b.Vel.X, 9);
        // Swerve: along-aim spin component −a·sinθ·(5V/2R) on the X (aim) axis.
        Assert.Equal(-side * Math.Sin(elev) * 2.5 * v / P.R, b.AngVel.X, 9);
        Assert.Equal(-side * Math.Cos(elev) * 2.5 * v / P.R, b.AngVel.Z, 9);
    }

    [Fact]
    public void MiscueLimit_ClampsExcessiveOffsets()
    {
        var reckless = Struck(Shot(side: 0.5, vert: 0.5)); // |offset| = 0.707 > 0.5
        var legal = Struck(Shot(side: 0.5 / Math.Sqrt(2.0), vert: 0.5 / Math.Sqrt(2.0)));

        Assert.Equal(legal.AngVel.Y, reckless.AngVel.Y, 9);
        Assert.Equal(legal.AngVel.Z, reckless.AngVel.Z, 9);
    }

    [Fact]
    public void Squirt_DeflectsAimAwayFromEnglishSide()
    {
        var withSquirt = Struck(Shot(side: 0.4), squirt: true);

        // Left english (a>0) squirts the ball to the right → negative Y velocity.
        Assert.True(withSquirt.Vel.Y < 0.0);
        Assert.Equal(2.0, withSquirt.Vel.Length, 9); // deflection rotates, never slows
    }

    [Fact]
    public void AimAngle_RotatesVelocityAndSpinTogether()
    {
        var shot = Shot(angleRad: Math.PI / 3.0, speed: 2.0, vert: 0.3);
        var b = Struck(shot);

        // Compare against the DECODED angle — quantization to microradians is intentional.
        var angle = shot.AimAngleRad;
        Assert.Equal(2.0 * Math.Cos(angle), b.Vel.X, 9);
        Assert.Equal(2.0 * Math.Sin(angle), b.Vel.Y, 9);
        // Pure follow spin must stay perpendicular to the velocity.
        Assert.Equal(0.0, b.AngVel.Xy.Dot(b.Vel), 9);
        // And oriented so the contact point slips backward (topspin), not forward.
        Assert.True(BallMotion.SlipVelocity(b, P.R).Dot(b.Vel) < b.Vel.LengthSquared);
    }
}
