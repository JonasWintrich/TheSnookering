using System;
using Snookering.Core.Mathematics;
using Snookering.Core.Tables;

namespace Snookering.Core.Physics;

/// <summary>
/// Time-of-impact solvers (linear motion within a tick — exact enough at 240 Hz,
/// where friction changes velocity by &lt; 0.01 m/s per tick) and impulse resolvers.
///
/// Resolver physics:
///  - Ball-ball: normal restitution + capped tangential Coulomb friction at the
///    contact point. The in-plane tangential slip comes from relative velocity and
///    vertical spin (ωz), so cut-induced throw, spin-induced throw, and english
///    transfer all emerge from one impulse.
///  - Cushion: contact sits 0.27R ABOVE ball center (nose height 63.5% of ball
///    diameter), so the normal impulse's torque bleeds topspin, and the friction
///    impulse couples english into the rebound angle — Han-2005 style, simplified.
/// </summary>
public static class Collisions
{
    /// <summary>Relative normal speeds below this are resting contact, not collisions.</summary>
    public const double ApproachEpsilon = 1e-6;

    private const double NoImpact = double.PositiveInfinity;

    // ---------------------------------------------------------------- TOI queries

    /// <summary>Earliest t in [0, maxT] when the two ball surfaces touch while approaching, else +∞.</summary>
    public static double BallBallToi(in BallState a, in BallState b, double r, double maxT)
    {
        var d = b.Pos - a.Pos;
        var w = b.Vel - a.Vel;
        var rr = 2.0 * r;

        var c = d.LengthSquared - rr * rr;
        var bq = d.Dot(w);
        if (bq >= 0.0)
            return NoImpact; // separating or parallel

        if (c <= 0.0)
            return 0.0; // already overlapping and approaching

        var aq = w.LengthSquared;
        var disc = bq * bq - aq * c;
        if (disc <= 0.0)
            return NoImpact;

        var t = (-bq - Math.Sqrt(disc)) / aq;
        return t >= 0.0 && t <= maxT ? t : NoImpact;
    }

    /// <summary>
    /// Earliest t in [0, maxT] when the ball touches the cushion face or one of its
    /// endpoints (noses) while approaching. Outputs the contact normal (toward the ball).
    /// </summary>
    public static double BallSegmentToi(in BallState ball, in CushionSegment seg, double r, double maxT, out Vec2 normal)
    {
        normal = seg.N;
        var best = NoImpact;

        // Face contact: signed distance to the infinite line along N reaches r.
        var vn = ball.Vel.Dot(seg.N);
        if (vn < -ApproachEpsilon)
        {
            var s0 = (ball.Pos - seg.A).Dot(seg.N);
            if (s0 >= r) // only from the playfield side
            {
                var t = (s0 - r) / -vn;
                if (t >= 0.0 && t <= maxT)
                {
                    // Contact point must project onto the segment.
                    var hit = ball.Pos + ball.Vel * t - r * seg.N;
                    var along = (hit - seg.A).Dot(seg.Dir);
                    if (along >= 0.0 && along <= seg.Length)
                        best = t;
                }
            }
        }

        // Nose (endpoint) contact: swept circle vs point.
        var ta = PointCircleToi(ball.Pos, ball.Vel, seg.A, r, maxT);
        if (ta < best)
        {
            best = ta;
            normal = (ball.Pos + ball.Vel * ta - seg.A).Normalized();
        }
        var tb = PointCircleToi(ball.Pos, ball.Vel, seg.B, r, maxT);
        if (tb < best)
        {
            best = tb;
            normal = (ball.Pos + ball.Vel * tb - seg.B).Normalized();
        }

        return best;
    }

    /// <summary>Earliest t when the ball touches the outside of a jaw arc within its angular range.</summary>
    public static double BallArcToi(in BallState ball, in JawArc arc, double r, double maxT, out Vec2 normal)
    {
        normal = Vec2.Zero;
        var t = SweptCircleToi(ball.Pos, ball.Vel, arc.Center, arc.Radius + r, maxT);
        if (t >= NoImpact)
            return NoImpact;

        var u = (ball.Pos + ball.Vel * t - arc.Center).Normalized();
        if (!arc.ContainsDirection(u))
            return NoImpact;

        normal = u;
        return t;
    }

    /// <summary>Earliest t when the ball CENTER enters the pocket fall circle.</summary>
    public static double PocketCaptureToi(in BallState ball, in Pocket pocket, double maxT)
    {
        var d = ball.Pos - pocket.FallCenter;
        if (d.LengthSquared <= pocket.FallRadius * pocket.FallRadius)
            return 0.0;
        return SweptCircleToi(ball.Pos, ball.Vel, pocket.FallCenter, pocket.FallRadius, maxT);
    }

    /// <summary>Swept point vs circle boundary (entering), used for capture and nose contacts.</summary>
    private static double SweptCircleToi(Vec2 pos, Vec2 vel, Vec2 center, double radius, double maxT)
    {
        var d = pos - center;
        var bq = d.Dot(vel);
        if (bq >= 0.0)
            return NoImpact;

        var aq = vel.LengthSquared;
        var c = d.LengthSquared - radius * radius;
        if (c <= 0.0)
            return 0.0;

        var disc = bq * bq - aq * c;
        if (disc <= 0.0)
            return NoImpact;

        var t = (-bq - Math.Sqrt(disc)) / aq;
        return t >= 0.0 && t <= maxT ? t : NoImpact;
    }

    private static double PointCircleToi(Vec2 pos, Vec2 vel, Vec2 point, double r, double maxT) =>
        SweptCircleToi(pos, vel, point, r, maxT);

    // ---------------------------------------------------------------- resolvers

    /// <summary>Resolve a ball-ball impact. Returns the relative approach speed (for audio/event data).</summary>
    public static double ResolveBallBall(ref BallState a, ref BallState b, PhysicsParams p)
    {
        var n = (b.Pos - a.Pos).Normalized();
        if (n == Vec2.Zero)
            n = new Vec2(1.0, 0.0); // degenerate exact overlap; deterministic fallback

        SeparateOverlap(ref a, ref b, n, p.R);

        var approach = (a.Vel - b.Vel).Dot(n);
        if (approach <= ApproachEpsilon)
            return 0.0;

        // Normal impulse (equal masses, effective mass m/2), applied +n on b, −n on a.
        var jn = (1.0 + p.BallBallRestitution) * 0.5 * p.Mass * approach;

        // In-plane tangential slip of a's surface relative to b's at the contact:
        // spin contributes only through ωz (vertical slip from follow/draw is absorbed).
        var t = n.Perp;
        var slip = (a.Vel - b.Vel).Dot(t) + p.R * (a.AngVel.Z + b.AngVel.Z);

        // Stick cap: effective tangential mass for two spheres is m/7.
        var jtMax = p.BallBallFriction * jn;
        var jtStick = Math.Abs(slip) * p.Mass / 7.0;
        var jt = Math.Sign(slip) * Math.Min(jtMax, jtStick);

        var invM = 1.0 / p.Mass;
        a.Vel -= (jn * invM) * n + (jt * invM) * t;
        b.Vel += (jn * invM) * n + (jt * invM) * t;

        // Friction torque acts about the vertical axis only (in-plane impulse at an in-plane contact).
        // τ = R·n × (∓jt·t̂) ⇒ Δωz = ∓ jt·R / I,  I = 2/5·m·R².
        var dwz = jt * p.R / (0.4 * p.Mass * p.R * p.R);
        a.AngVel = new Vec3(a.AngVel.X, a.AngVel.Y, a.AngVel.Z - dwz);
        b.AngVel = new Vec3(b.AngVel.X, b.AngVel.Y, b.AngVel.Z - dwz);

        a.State = MotionState.Sliding;
        b.State = MotionState.Sliding;
        return approach;
    }

    /// <summary>Resolve a ball-cushion impact against contact normal n. Returns impact speed.</summary>
    public static double ResolveCushion(ref BallState ball, Vec2 n, PhysicsParams p)
    {
        var vn = ball.Vel.Dot(n);
        if (vn >= -ApproachEpsilon)
            return 0.0;

        // Contact point: on the far side of the ball, 0.27R above center (cushion nose height).
        var r3 = new Vec3(-p.R * n.X, -p.R * n.Y, 0.27 * p.R);
        var v3 = new Vec3(ball.Vel.X, ball.Vel.Y, 0.0);
        var n3 = new Vec3(n.X, n.Y, 0.0);

        var jn = -(1.0 + p.CushionRestitution) * p.Mass * vn;

        // Contact-point velocity and its tangential (non-normal) component.
        var u = v3 + ball.AngVel.Cross(r3);
        var slip = u - u.Dot(n3) * n3;
        var slipLen = slip.Length;

        var inertia = 0.4 * p.Mass * p.R * p.R;
        Vec3 impulse = jn * n3;
        if (slipLen > 1e-12)
        {
            // Stick cap for a single sphere contact: effective tangential mass 2m/7.
            var jt = Math.Min(p.CushionFriction * jn, 2.0 * p.Mass * slipLen / 7.0);
            impulse -= (jt / slipLen) * slip;
        }

        // Linear response is horizontal only (the table absorbs vertical impulse);
        // the torque keeps the full 3D impulse so topspin bleed and english coupling emerge.
        ball.Vel += new Vec2(impulse.X, impulse.Y) * (1.0 / p.Mass);
        ball.AngVel += r3.Cross(impulse) * (1.0 / inertia);
        ball.State = MotionState.Sliding;
        return -vn;
    }

    private static void SeparateOverlap(ref BallState a, ref BallState b, Vec2 n, double r)
    {
        var dist = (b.Pos - a.Pos).Length;
        var overlap = 2.0 * r - dist;
        if (overlap > 0.0)
        {
            var half = 0.5 * overlap + 1e-12;
            a.Pos -= half * n;
            b.Pos += half * n;
        }
    }
}
