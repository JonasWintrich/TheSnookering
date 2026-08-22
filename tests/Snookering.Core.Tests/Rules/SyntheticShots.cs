using System.Collections.Generic;
using System.Linq;
using Snookering.Core.Mathematics;
using Snookering.Core.Physics;

namespace Snookering.Core.Tests.Rules;

/// <summary>Builds synthetic ShotResults for rules tests — no physics involved.</summary>
public sealed class SyntheticShot
{
    private readonly List<SimEvent> _events = new() { new SimEvent(0.0, SimEventType.CueStrike, 0, 0, -1, 2.0) };
    private double _t = 0.1;

    public SyntheticShot Contact(byte ball, byte by = 0)
    {
        _events.Add(new SimEvent(_t += 0.1, SimEventType.BallBall, by, ball, -1, 1.0));
        return this;
    }

    public SyntheticShot Rail(byte ball = 0)
    {
        _events.Add(new SimEvent(_t += 0.1, SimEventType.Cushion, ball, ball, 100, 1.0));
        return this;
    }

    public SyntheticShot Pot(byte ball)
    {
        _events.Add(new SimEvent(_t += 0.1, SimEventType.Pocketed, ball, ball, 0, 1.0));
        return this;
    }

    public ShotResult Result(TableState final)
    {
        _events.Add(new SimEvent(_t += 0.1, SimEventType.RestReached, 0, 0, -1, 0.0));
        return new ShotResult
        {
            FinalState = final,
            Events = _events,
            Frames = new List<TrajectoryFrame>(),
            StateHash = 0,
            Duration = _t,
        };
    }

    /// <summary>A table state with the given ids present; balls named in `potted` are off the table.</summary>
    public static TableState State(int ballCount, params byte[] offTable)
    {
        var balls = new BallState[ballCount];
        for (byte id = 0; id < ballCount; id++)
        {
            balls[id] = BallState.AtRest(id, new Vec2(0.09 * id - 0.9, 0.12 * (id % 3)));
            balls[id].OnTable = !offTable.Contains(id);
        }
        return new TableState(balls);
    }
}
