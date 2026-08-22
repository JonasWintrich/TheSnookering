namespace Snookering.Core.Physics;

public enum SimEventType : byte
{
    CueStrike = 0,
    BallBall = 1,
    Cushion = 2,
    Pocketed = 3,
    RestReached = 4,
}

/// <summary>
/// One thing that happened during a shot. The rules engine adjudicates from the
/// ordered event list; presentation triggers audio/VFX from it (Speed → volume).
/// FeatureId: cushion/jaw feature for Cushion events, pocket id for Pocketed.
/// </summary>
public readonly record struct SimEvent(
    double Time,
    SimEventType Type,
    byte BallA,
    byte BallB,
    short FeatureId,
    double Speed);
