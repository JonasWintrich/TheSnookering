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

            Resolve(balls, p, ev, tickStart + (Dt - remaining), events);

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

    private static void Resolve(BallState[] balls, PhysicsParams p, in PendingEvent ev, double time, List<SimEvent> events)
    {
        switch (ev.Type)
        {
            case SimEventType.BallBall:
            {
                var speed = Collisions.ResolveBallBall(ref balls[ev.IndexA], ref balls[ev.IndexB], p);
                if (speed > 0.0)
                    events.Add(new SimEvent(time, SimEventType.BallBall, balls[ev.IndexA].Id, balls[ev.IndexB].Id, -1, speed));
                break;
            }
            case SimEventType.Cushion:
            {
                var speed = Collisions.ResolveCushion(ref balls[ev.IndexA], ev.Normal, p);
                if (speed > 0.0)
                    events.Add(new SimEvent(time, SimEventType.Cushion, balls[ev.IndexA].Id, balls[ev.IndexA].Id, ev.FeatureId, speed));
                break;
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
                break;
            }
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
