using System;
using System.Collections.Generic;
using Snookering.Core.Mathematics;
using Snookering.Core.Tables;

namespace Snookering.Core.Physics;

/// <summary>
/// The deterministic shot simulator: a pure function
///     (initial state, shot input, table) → ShotResult.
///
/// Fixed 240 Hz ticks; within each tick the earliest collision/capture event is
/// found analytically (linear TOI — friction shifts velocity &lt; 0.01 m/s per tick),
/// all balls advance exactly to it via the friction-aware integrator, the impulse
/// is resolved, and the remainder of the tick repeats the process. No tunneling at
/// any legal speed, crisp jaw rattles, and bit-reproducible runs.
/// </summary>
public static class Simulator
{
    public const double Dt = 1.0 / 240.0;
    public const int TrajectorySampleEveryTicks = 4; // 60 Hz
    public const double MaxShotSeconds = 60.0;
    private const int MaxEventsPerTick = 64;

    /// <summary>Run one complete shot. cueBallId names the ball receiving the strike.</summary>
    public static ShotResult Run(TableState initial, in ShotInput shot, TableSpec table, byte cueBallId = 0)
    {
        var state = initial.Clone();
        var balls = state.Balls;
        var p = table.Physics;
        var events = new List<SimEvent>(64);
        var frames = new List<TrajectoryFrame>(512);
        var hash = Fnv1A.Offset;

        ref var cue = ref state.Ball(cueBallId);
        CueStrike.Apply(ref cue, shot, p);
        events.Add(new SimEvent(0.0, SimEventType.CueStrike, cueBallId, cueBallId, -1, cue.Vel.Length));

        var time = 0.0;
        frames.Add(Sample(0.0, balls));

        var tick = 0;
        while (state.AnyActive && time < MaxShotSeconds)
        {
            StepTick(balls, table, p, time, events);
            time += Dt;
            tick++;
            ResolvePenetrations(balls, table, p, time, events);
            CaptureEscapees(balls, table, time, events);

            for (var i = 0; i < balls.Length; i++)
                hash = Fnv1A.Add(hash, in balls[i]);

            if (tick % TrajectorySampleEveryTicks == 0)
                frames.Add(Sample(time, balls));
        }

        frames.Add(Sample(time, balls));
        events.Add(new SimEvent(time, SimEventType.RestReached, 0, 0, -1, 0.0));

        return new ShotResult
        {
            FinalState = state,
            Events = events,
            Frames = frames,
            StateHash = hash,
            Duration = time,
        };
    }

    /// <summary>
    /// Push balls back out of any cushion they have sunk into.
    ///
    /// Ball-ball separation moves balls apart without knowing about cushions, so a
    /// ball squeezed against a rail inside a tight pack gets shoved into it. Once
    /// its centre is closer to the face than one radius, <see cref="Collisions.BallSegmentToi"/>
    /// stops reporting that cushion at all — it only considers approaches from the
    /// playfield side — and from then on the ball travels straight through the
    /// rail. This pass is what keeps the table solid.
    ///
    /// Pocket mouths are unaffected: there is no cushion segment spanning them, so
    /// a ball on its way into a pocket is never pushed back out.
    /// </summary>
    private static void ResolvePenetrations(BallState[] balls, TableSpec table, PhysicsParams p, double time, List<SimEvent> events)
    {
        var r = p.R;
        for (var i = 0; i < balls.Length; i++)
        {
            ref var ball = ref balls[i];
            if (!ball.OnTable)
                continue;

            for (var s = 0; s < table.Cushions.Count; s++)
            {
                var seg = table.Cushions[s];
                var signed = (ball.Pos - seg.A).Dot(seg.N);
                var depth = r - signed;
                if (depth <= 0.0)
                    continue;

                // Only when the contact really lies on this segment; past its ends
                // the nose test below (or the open pocket mouth) applies instead.
                var foot = ball.Pos - signed * seg.N;
                var along = (foot - seg.A).Dot(seg.Dir);
                if (along < 0.0 || along > seg.Length)
                    continue;

                ball.Pos += depth * seg.N;
                PushOff(ref ball, seg.N, p, seg.FeatureId, time, events);
            }

            for (var s = 0; s < table.Cushions.Count; s++)
            {
                var seg = table.Cushions[s];
                PushOffPoint(ref ball, seg.A, r, p, seg.FeatureId, time, events);
                PushOffPoint(ref ball, seg.B, r, p, seg.FeatureId, time, events);
            }

            for (var j = 0; j < table.Jaws.Count; j++)
            {
                var arc = table.Jaws[j];
                var d = ball.Pos - arc.Center;
                var dist = d.Length;
                var minimum = arc.Radius + r;
                if (dist >= minimum || dist <= 1e-9)
                    continue;
                var n = d / dist;
                if (!arc.ContainsDirection(n))
                    continue;

                ball.Pos += (minimum - dist) * n;
                PushOff(ref ball, n, p, arc.FeatureId, time, events);
            }
        }
    }

    private static void PushOffPoint(ref BallState ball, Vec2 point, double r, PhysicsParams p,
        short featureId, double time, List<SimEvent> events)
    {
        var d = ball.Pos - point;
        var dist = d.Length;
        if (dist >= r || dist <= 1e-9)
            return;
        var n = d / dist;
        ball.Pos += (r - dist) * n;
        PushOff(ref ball, n, p, featureId, time, events);
    }

    private static void PushOff(ref BallState ball, Vec2 normal, PhysicsParams p,
        short featureId, double time, List<SimEvent> events)
    {
        if (ball.Vel.Dot(normal) >= 0.0)
            return; // already leaving; the position fix is enough
        var speed = Collisions.ResolveCushion(ref ball, normal, p);
        if (speed > 0.0)
            events.Add(new SimEvent(time, SimEventType.Cushion, ball.Id, ball.Id, featureId, speed));
    }

    /// <summary>
    /// Containment invariant. A ball's centre can only pass outside the playfield
    /// rectangle through a pocket mouth — the cushions stop it everywhere else —
    /// so once it is half a radius beyond that line it is in the pocket and not
    /// coming back. Without this, a ball that threads a mouth without clipping a
    /// jaw or reaching the fall circle leaves the table entirely, and the rules
    /// engine then adjudicates a state that cannot physically exist.
    ///
    /// The margin is what preserves jaw rattles: a ball bouncing in the jaws is
    /// still inside or barely past the line, and stays in play.
    /// </summary>
    private static void CaptureEscapees(BallState[] balls, TableSpec table, double time, List<SimEvent> events)
    {
        var margin = table.Physics.R * 0.5;
        for (var i = 0; i < balls.Length; i++)
        {
            ref var ball = ref balls[i];
            if (!ball.OnTable)
                continue;
            if (Math.Abs(ball.Pos.X) <= table.HalfLength + margin &&
                Math.Abs(ball.Pos.Y) <= table.HalfWidth + margin)
                continue;

            short pocketId = 0;
            var nearest = double.MaxValue;
            for (var k = 0; k < table.Pockets.Count; k++)
            {
                var d = (table.Pockets[k].FallCenter - ball.Pos).LengthSquared;
                if (d < nearest)
                {
                    nearest = d;
                    pocketId = table.Pockets[k].Id;
                }
            }

            var speed = ball.Vel.Length;
            ball.OnTable = false;
            ball.Vel = Vec2.Zero;
            ball.AngVel = Vec3.Zero;
            ball.State = MotionState.Stationary;
            events.Add(new SimEvent(time, SimEventType.Pocketed, ball.Id, ball.Id, pocketId, speed));
        }
    }

    private static void StepTick(BallState[] balls, TableSpec table, PhysicsParams p, double tickStart, List<SimEvent> events)
    {
        var remaining = Dt;
        var guard = 0;

        while (remaining > 1e-12)
        {
            var toi = FindEarliestEvent(balls, table, p, remaining, out var ev);
            var step = Math.Min(toi, remaining);

            if (step > 0.0)
                for (var i = 0; i < balls.Length; i++)
                    BallMotion.Integrate(ref balls[i], p, step);

            remaining -= step;

            if (toi > remaining + step)
                break; // no event within the tick — fully integrated

            var progressed = Resolve(balls, p, ev, tickStart + (Dt - remaining), events);

            // Zero-progress guard: a zero-time event that applied no impulse (a contact
            // that stopped approaching) would be re-selected forever. Consume the rest
            // of the tick with plain motion instead; the situation changes next tick.
            if (!progressed && step <= 0.0)
            {
                for (var i = 0; i < balls.Length; i++)
                    BallMotion.Integrate(ref balls[i], p, remaining);
                break;
            }

            if (++guard >= MaxEventsPerTick)
                break; // pathological cluster; stop resolving this tick, motion continues next tick
        }
    }

    private readonly struct PendingEvent
    {
        public required SimEventType Type { get; init; }
        public required int IndexA { get; init; }
        public required int IndexB { get; init; }
        public required short FeatureId { get; init; }
        public required Vec2 Normal { get; init; }
    }

    private static double FindEarliestEvent(BallState[] balls, TableSpec table, PhysicsParams p, double maxT, out PendingEvent ev)
    {
        var best = double.PositiveInfinity;
        ev = default;

        for (var i = 0; i < balls.Length; i++)
        {
            ref var a = ref balls[i];
            if (!a.OnTable)
                continue;

            var moving = a.State != MotionState.Stationary;

            // Ball-ball: check each pair once (j > i); either may be the mover.
            for (var j = i + 1; j < balls.Length; j++)
            {
                ref var b = ref balls[j];
                if (!b.OnTable || (!moving && b.State == MotionState.Stationary))
                    continue;

                var t = Collisions.BallBallToi(in a, in b, p.R, maxT);
                if (t < best)
                {
                    best = t;
                    ev = new PendingEvent { Type = SimEventType.BallBall, IndexA = i, IndexB = j, FeatureId = -1, Normal = Vec2.Zero };
                }
            }

            if (!moving)
                continue;

            for (var s = 0; s < table.Cushions.Count; s++)
            {
                var seg = table.Cushions[s];
                var t = Collisions.BallSegmentToi(in a, seg, p.R, maxT, out var n);
                if (t < best)
                {
                    best = t;
                    ev = new PendingEvent { Type = SimEventType.Cushion, IndexA = i, IndexB = i, FeatureId = seg.FeatureId, Normal = n };
                }
            }

            for (var s = 0; s < table.Jaws.Count; s++)
            {
                var arc = table.Jaws[s];
                var t = Collisions.BallArcToi(in a, arc, p.R, maxT, out var n);
                if (t < best)
                {
                    best = t;
                    ev = new PendingEvent { Type = SimEventType.Cushion, IndexA = i, IndexB = i, FeatureId = arc.FeatureId, Normal = n };
                }
            }

            for (var s = 0; s < table.Pockets.Count; s++)
            {
                var pocket = table.Pockets[s];
                var t = Collisions.PocketCaptureToi(in a, pocket, maxT);
                if (t < best)
                {
                    best = t;
                    ev = new PendingEvent { Type = SimEventType.Pocketed, IndexA = i, IndexB = i, FeatureId = pocket.Id, Normal = Vec2.Zero };
                }
            }
        }

        return best;
    }

    /// <summary>Returns true when the event actually changed state (impulse applied / ball pocketed).</summary>
    private static bool Resolve(BallState[] balls, PhysicsParams p, in PendingEvent ev, double time, List<SimEvent> events)
    {
        switch (ev.Type)
        {
            case SimEventType.BallBall:
            {
                var speed = Collisions.ResolveBallBall(ref balls[ev.IndexA], ref balls[ev.IndexB], p);
                if (speed <= 0.0)
                    return false;
                events.Add(new SimEvent(time, SimEventType.BallBall, balls[ev.IndexA].Id, balls[ev.IndexB].Id, -1, speed));
                return true;
            }
            case SimEventType.Cushion:
            {
                var speed = Collisions.ResolveCushion(ref balls[ev.IndexA], ev.Normal, p);
                if (speed <= 0.0)
                    return false;
                events.Add(new SimEvent(time, SimEventType.Cushion, balls[ev.IndexA].Id, balls[ev.IndexA].Id, ev.FeatureId, speed));
                return true;
            }
            case SimEventType.Pocketed:
            {
                ref var ball = ref balls[ev.IndexA];
                var speed = ball.Vel.Length;
                ball.OnTable = false;
                ball.Vel = Vec2.Zero;
                ball.AngVel = Vec3.Zero;
                ball.State = MotionState.Stationary;
                events.Add(new SimEvent(time, SimEventType.Pocketed, ball.Id, ball.Id, ev.FeatureId, speed));
                return true;
            }
            default:
                return false;
        }
    }

    private static TrajectoryFrame Sample(double time, BallState[] balls)
    {
        var samples = new BallSample[balls.Length];
        for (var i = 0; i < balls.Length; i++)
            samples[i] = new BallSample(balls[i].Id, balls[i].Pos, balls[i].AngVel, balls[i].OnTable);
        return new TrajectoryFrame(time, samples);
    }
}

/// <summary>FNV-1a over the raw bits of ball state — the determinism fingerprint.</summary>
public static class Fnv1A
{
    public const ulong Offset = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong Add(ulong hash, in BallState b)
    {
        hash = Add(hash, b.Pos.X);
        hash = Add(hash, b.Pos.Y);
        hash = Add(hash, b.Vel.X);
        hash = Add(hash, b.Vel.Y);
        hash = Add(hash, b.AngVel.X);
        hash = Add(hash, b.AngVel.Y);
        hash = Add(hash, b.AngVel.Z);
        hash = (hash ^ (byte)b.State) * Prime;
        hash = (hash ^ (b.OnTable ? 1UL : 0UL)) * Prime;
        return hash;
    }

    public static ulong Add(ulong hash, double value)
    {
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        for (var i = 0; i < 8; i++)
        {
            hash ^= (bits >> (i * 8)) & 0xFF;
            hash *= Prime;
        }
        return hash;
    }
}
