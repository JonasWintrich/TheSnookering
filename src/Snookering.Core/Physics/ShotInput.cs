namespace Snookering.Core.Physics;

/// <summary>
/// The complete, quantized description of one shot — the only payload that ever
/// crosses the network in multiplayer. Integer quantization guarantees every peer
/// converts to identical doubles, absorbing any platform trig variance at the
/// input boundary (the sim itself contains no trig).
/// </summary>
public readonly record struct ShotInput
{
    /// <summary>Aim azimuth φ in microradians (1e-6 rad), world frame, CCW from +X.</summary>
    public required int AimAngleMicroRad { get; init; }

    /// <summary>Resulting cue-ball speed in mm/s (before elevation projection).</summary>
    public required int SpeedMmPerSec { get; init; }

    /// <summary>
    /// Sideways tip offset in 1e-4 fractions of ball radius R.
    /// Positive = tip offset to the LEFT of center as seen from behind the cue
    /// (left english → clockwise spin viewed from above, ωz &lt; 0).
    /// </summary>
    public required short OffsetSide1e4 { get; init; }

    /// <summary>Vertical tip offset in 1e-4 fractions of R. Positive = above center (follow).</summary>
    public required short OffsetVert1e4 { get; init; }

    /// <summary>Cue elevation above horizontal in centidegrees. v1 range: 0–1500 (0–15°).</summary>
    public required short ElevationCentiDeg { get; init; }

    /// <summary>Seed for any randomness attached to this shot (AI noise). The sim itself is exact.</summary>
    public ulong Seed { get; init; }

    /// <summary>
    /// Where the cue ball is placed before the shot, in micrometres from the table
    /// centre, when the player has ball in hand. Null means "play it where it lies".
    ///
    /// Ball-in-hand used to be applied straight to the table state, outside this
    /// struct, which quietly made the claim above false: after a foul, the shot was
    /// no longer fully described by its input. Carrying the placement here keeps one
    /// turn to one message. It is quantized like every other field because the
    /// position originates in a floating-point camera raycast, and two peers must
    /// reconstruct bit-identical doubles from it.
    /// </summary>
    public int? CuePlaceXMicroM { get; init; }

    public int? CuePlaceYMicroM { get; init; }

    public bool HasCuePlacement => CuePlaceXMicroM.HasValue && CuePlaceYMicroM.HasValue;

    public Mathematics.Vec2 CuePlacement =>
        new(CuePlaceXMicroM.GetValueOrDefault() * 1e-6, CuePlaceYMicroM.GetValueOrDefault() * 1e-6);

    /// <summary>Quantize a placement so every peer derives the same double.</summary>
    public static (int X, int Y) QuantizePlacement(Mathematics.Vec2 pos) =>
        ((int)System.Math.Round(pos.X * 1e6), (int)System.Math.Round(pos.Y * 1e6));

    public double AimAngleRad => AimAngleMicroRad * 1e-6;
    public double Speed => SpeedMmPerSec * 1e-3;
    public double OffsetSide => OffsetSide1e4 * 1e-4;
    public double OffsetVert => OffsetVert1e4 * 1e-4;
    public double ElevationRad => ElevationCentiDeg * (System.Math.PI / 18000.0);
}
