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

    public double AimAngleRad => AimAngleMicroRad * 1e-6;
    public double Speed => SpeedMmPerSec * 1e-3;
    public double OffsetSide => OffsetSide1e4 * 1e-4;
    public double OffsetVert => OffsetVert1e4 * 1e-4;
    public double ElevationRad => ElevationCentiDeg * (System.Math.PI / 18000.0);
}
