using System;
using Snookering.Core.Mathematics;

namespace Snookering.Core.Physics;

/// <summary>
/// Cloth-interaction motion model: Sliding → Rolling → Stationary with Coulomb
/// friction, plus independent vertical-spin (english) decay.
///
/// Within one tick the friction direction is frozen (valid at 240 Hz), which makes
/// each phase constant-acceleration. Phase transitions (slip exhausted, ball stopped,
/// spin exhausted) are resolved at their EXACT times inside the tick, so the model
/// reproduces the closed-form billiards results to floating-point accuracy:
///   stun-shot slide time 2v₀/(7μₛg), post-slide speed 5v₀/7,
///   slide distance 12v₀²/(49μₛg), rolling stop distance v²/(2μᵣg).
/// </summary>
public static class BallMotion
{
    /// <summary>
    /// Velocity of the ball's contact point relative to the cloth.
    /// Contact point is at −R·ẑ, so u = v + ω × (−R·ẑ) = (vx − R·ωy, vy + R·ωx).
    /// </summary>
    public static Vec2 SlipVelocity(in BallState b, double r) =>
        new(b.Vel.X - r * b.AngVel.Y, b.Vel.Y + r * b.AngVel.X);

    /// <summary>Advance one ball by dt seconds of cloth interaction (no collisions).</summary>
    public static void Integrate(ref BallState b, PhysicsParams p, double dt)
    {
        if (!b.OnTable)
            return;

        var remaining = dt;
        while (remaining > 0.0)
        {
            switch (b.State)
            {
                case MotionState.Sliding:
                    remaining = StepSliding(ref b, p, remaining);
                    break;
                case MotionState.Rolling:
                    remaining = StepRolling(ref b, p, remaining);
                    break;
                default:
                    b.AngVel = new Vec3(0.0, 0.0, DecayVerticalSpin(b.AngVel.Z, p, remaining));
                    return;
            }
        }
    }

    private static double StepSliding(ref BallState b, PhysicsParams p, double remaining)
    {
        var u = SlipVelocity(b, p.R);
        var uLen = u.Length;
        if (uLen <= p.SlipEpsilon)
        {
            SnapToRolling(ref b, p);
            return remaining;
        }

        var uHat = u / uLen;

        // Slip speed decays at 7/2·μₛ·g (translation μₛg + rotation 5/2·μₛg both oppose slip).
        var slipDecel = 3.5 * p.MuSlide * p.G;
        var slideTime = uLen / slipDecel;
        var t = Math.Min(slideTime, remaining);

        var a = p.MuSlide * p.G; // translational deceleration magnitude
        b.Pos += b.Vel * t - (0.5 * a * t * t) * uHat;
        b.Vel -= (a * t) * uHat;

        // dω/dt = (5μₛg / 2R) · (ẑ × û),  ẑ × û = (−ûy, ûx)
        var k = 2.5 * p.MuSlide * p.G / p.R;
        b.AngVel = new Vec3(
            b.AngVel.X - k * t * uHat.Y,
            b.AngVel.Y + k * t * uHat.X,
            DecayVerticalSpin(b.AngVel.Z, p, t));

        if (t >= slideTime)
            SnapToRolling(ref b, p);

        return remaining - t;
    }

    private static double StepRolling(ref BallState b, PhysicsParams p, double remaining)
    {
        var speed = b.Vel.Length;
        if (speed <= p.RestEpsilon)
        {
            SnapToRest(ref b);
            return remaining;
        }

        var vHat = b.Vel / speed;
        var a = p.MuRoll * p.G;
        var stopTime = speed / a;
        var t = Math.Min(stopTime, remaining);

        b.Pos += b.Vel * t - (0.5 * a * t * t) * vHat;
        b.Vel -= (a * t) * vHat;

        if (t >= stopTime)
        {
            SnapToRest(ref b);
        }
        else
        {
            // Natural roll: horizontal spin slaved to velocity (contact point at rest).
            b.AngVel = new Vec3(
                -b.Vel.Y / p.R,
                b.Vel.X / p.R,
                DecayVerticalSpin(b.AngVel.Z, p, t));
        }

        return remaining - t;
    }

    /// <summary>Kill slip exactly: horizontal spin takes the rolling-constraint values.</summary>
    private static void SnapToRolling(ref BallState b, PhysicsParams p)
    {
        b.AngVel = new Vec3(-b.Vel.Y / p.R, b.Vel.X / p.R, b.AngVel.Z);
        b.State = b.Vel.LengthSquared > 0.0 ? MotionState.Rolling : MotionState.Stationary;
    }

    private static void SnapToRest(ref BallState b)
    {
        b.Vel = Vec2.Zero;
        b.AngVel = new Vec3(0.0, 0.0, b.AngVel.Z);
        b.State = MotionState.Stationary;
    }

    /// <summary>English decays linearly at 5μₛₚg/2R and stops exactly at zero.</summary>
    private static double DecayVerticalSpin(double wz, PhysicsParams p, double t)
    {
        if (wz == 0.0)
            return 0.0;
        var rate = 2.5 * p.MuSpin * p.G / p.R;
        var magnitude = Math.Abs(wz) - rate * t;
        return magnitude <= 0.0 ? 0.0 : Math.Sign(wz) * magnitude;
    }
}
