namespace Snookering.Core.Physics;

/// <summary>
/// All tunable simulation coefficients in one place. Values seeded from published
/// billiards physics (Alciatore / pooltool); tuned for feel during M1.
/// </summary>
public sealed record PhysicsParams
{
    /// <summary>Gravitational acceleration, m/s².</summary>
    public double G { get; init; } = 9.81;

    /// <summary>Ball radius, m.</summary>
    public required double R { get; init; }

    /// <summary>Ball mass, kg.</summary>
    public required double Mass { get; init; }

    /// <summary>Cloth sliding (kinetic) friction coefficient.</summary>
    public double MuSlide { get; init; } = 0.20;

    /// <summary>Rolling resistance coefficient.</summary>
    public required double MuRoll { get; init; }

    /// <summary>Vertical-spin (english) decay friction coefficient.</summary>
    public double MuSpin { get; init; } = 0.044;

    /// <summary>Ball-ball normal restitution (phenolic resin ≈ 0.92–0.96).</summary>
    public double BallBallRestitution { get; init; } = 0.94;

    /// <summary>Ball-ball tangential Coulomb friction — produces throw and spin transfer.</summary>
    public double BallBallFriction { get; init; } = 0.05;

    /// <summary>Cushion normal restitution.</summary>
    public double CushionRestitution { get; init; } = 0.80;

    /// <summary>Cushion tangential friction — produces english response off rails.</summary>
    public double CushionFriction { get; init; } = 0.20;

    /// <summary>
    /// Slip speed below which sliding snaps to rolling, m/s. Phase transitions are
    /// resolved at exact times, so this is only a numerical safety guard — keep tiny,
    /// a coarse value truncates real travel distance.
    /// </summary>
    public double SlipEpsilon { get; init; } = 1e-9;

    /// <summary>Ball speed below which rolling snaps to rest, m/s. Safety guard only — see SlipEpsilon.</summary>
    public double RestEpsilon { get; init; } = 1e-9;

    /// <summary>WPA 2¼-inch pool ball on standard cloth.</summary>
    public static PhysicsParams Pool() => new()
    {
        R = 0.028575,
        Mass = 0.170,
        MuRoll = 0.010,
    };

    /// <summary>52.5 mm snooker ball on fast napped cloth.</summary>
    public static PhysicsParams Snooker() => new()
    {
        R = 0.02625,
        Mass = 0.142,
        MuRoll = 0.008,
    };
}
