using System.Collections.Generic;
using Snookering.Core.Mathematics;

namespace Snookering.Core.Physics;

/// <summary>Per-ball sample within one trajectory frame.</summary>
public readonly record struct BallSample(byte Id, Vec2 Pos, Vec3 AngVel, bool OnTable);

/// <summary>Snapshot of every ball at one sample time (60 Hz), for presentation playback.</summary>
public readonly struct TrajectoryFrame
{
    public readonly double Time;
    public readonly BallSample[] Balls;

    public TrajectoryFrame(double time, BallSample[] balls)
    {
        Time = time;
        Balls = balls;
    }
}

/// <summary>
/// Everything a shot produced: the final state (authoritative), the ordered event
/// log (rules + audio), the sampled trajectory (rendering), and a determinism hash
/// over the full per-tick state history (golden tests, multiplayer divergence checks).
/// </summary>
public sealed class ShotResult
{
    public required TableState FinalState { get; init; }
    public required IReadOnlyList<SimEvent> Events { get; init; }
    public required IReadOnlyList<TrajectoryFrame> Frames { get; init; }
    public required ulong StateHash { get; init; }
    public required double Duration { get; init; }
}
