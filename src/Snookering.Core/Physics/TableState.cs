using System;

namespace Snookering.Core.Physics;

/// <summary>All ball states at one instant. Fixed array, fixed iteration order — determinism.</summary>
public sealed class TableState
{
    public readonly BallState[] Balls;

    public TableState(BallState[] balls) => Balls = balls;

    public TableState Clone()
    {
        var copy = new BallState[Balls.Length];
        Array.Copy(Balls, copy, Balls.Length);
        return new TableState(copy);
    }

    public bool AnyActive
    {
        get
        {
            for (var i = 0; i < Balls.Length; i++)
                if (Balls[i].IsActive)
                    return true;
            return false;
        }
    }

    public ref BallState Ball(byte id)
    {
        for (var i = 0; i < Balls.Length; i++)
            if (Balls[i].Id == id)
                return ref Balls[i];
        throw new ArgumentException($"no ball with id {id}");
    }
}
