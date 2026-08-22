using System.Collections.Generic;
using Snookering.Core.Physics;

namespace Snookering.Core.Rules;

/// <summary>
/// Derived facts about one shot, extracted once from the event log and shared by
/// all rulesets. Rules never look at raw events — they adjudicate from these facts,
/// which also makes every rule unit-testable with a hand-written event list.
/// </summary>
public readonly struct ShotFacts
{
    /// <summary>Id of the first ball the cue ball contacted, or null if none.</summary>
    public required byte? FirstContact { get; init; }

    /// <summary>Object balls pocketed, in order (cue ball excluded).</summary>
    public required IReadOnlyList<byte> Potted { get; init; }

    public required bool CuePotted { get; init; }

    /// <summary>Any ball reached a cushion at or after the first cue-ball contact.</summary>
    public required bool RailAfterContact { get; init; }

    public static ShotFacts Extract(IReadOnlyList<SimEvent> events, byte cueId = 0)
    {
        byte? firstContact = null;
        var firstContactTime = double.PositiveInfinity;
        var potted = new List<byte>();
        var cuePotted = false;
        var railAfter = false;

        foreach (var e in events)
        {
            switch (e.Type)
            {
                case SimEventType.BallBall:
                    if (firstContact is null && (e.BallA == cueId || e.BallB == cueId))
                    {
                        firstContact = e.BallA == cueId ? e.BallB : e.BallA;
                        firstContactTime = e.Time;
                    }
                    break;

                case SimEventType.Cushion:
                    if (e.Time >= firstContactTime)
                        railAfter = true;
                    break;

                case SimEventType.Pocketed:
                    if (e.BallA == cueId)
                        cuePotted = true;
                    else
                        potted.Add(e.BallA);
                    break;
            }
        }

        return new ShotFacts
        {
            FirstContact = firstContact,
            Potted = potted,
            CuePotted = cuePotted,
            RailAfterContact = railAfter,
        };
    }
}
