using System;
using Snookering.Core.Mathematics;

namespace Snookering.Core.Physics;

/// <summary>
/// Converts a quantized <see cref="ShotInput"/> into the cue ball's initial state.
///
/// This is the ONE place trig is allowed: quantized inputs make the conversion
/// reproduce identically everywhere, and the sim downstream is trig-free.
///
/// Derivation (thin-cue impulse at the tip contact point, I = 2/5·mR²):
/// in the aim frame (x = aim, y = left, z = up), with side offset a, vertical
/// offset b (fractions of R) and elevation θ:
///     v  = V·cosθ · x̂                        (vertical impulse absorbed by the table; no jumps in v1)
///     ω  = (5V / 2R) · (−a·sinθ,  b,  −a·cosθ)
/// so b gives follow/draw, a gives english (ωz), and a·sinθ gives the along-aim
/// swerve component that curves a masse shot — all from one cross product.
/// </summary>
public static class CueStrike
{
    /// <summary>Maximum tip offset radius as a fraction of R (the miscue limit).</summary>
    public const double MiscueLimit = 0.5;

    /// <summary>Squirt (cue-ball deflection): aim correction per unit side offset, radians.</summary>
    public const double SquirtRadPerOffset = 0.02;

    public static void Apply(ref BallState cueBall, in ShotInput shot, PhysicsParams p, bool squirt = true)
    {
        var a = shot.OffsetSide;
        var b = shot.OffsetVert;

        // Defensive miscue clamp — UI and AI should never exceed it, the sim never trusts them.
        var offsetLen = Math.Sqrt(a * a + b * b);
        if (offsetLen > MiscueLimit)
        {
            var scale = MiscueLimit / offsetLen;
            a *= scale;
            b *= scale;
        }

        var phi = shot.AimAngleRad;
        if (squirt)
            phi -= SquirtRadPerOffset * a; // left-side tip (a>0) squirts the ball to the right

        var dir = new Vec2(Math.Cos(phi), Math.Sin(phi));
        var left = dir.Perp;

        var v = shot.Speed;
        var elevation = shot.ElevationRad;
        var cosE = Math.Cos(elevation);
        var sinE = Math.Sin(elevation);

        cueBall.Vel = (v * cosE) * dir;

        var spin = 2.5 * v / p.R;
        var wAim = -a * sinE * spin;  // swerve component (along aim)
        var wLeft = b * spin;         // follow/draw component (about the left axis)
        var wUp = -a * cosE * spin;   // english

        cueBall.AngVel = new Vec3(
            wAim * dir.X + wLeft * left.X,
            wAim * dir.Y + wLeft * left.Y,
            wUp);

        cueBall.State = MotionState.Sliding;
    }
}
