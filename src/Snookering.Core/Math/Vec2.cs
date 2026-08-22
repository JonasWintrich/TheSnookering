using System;

namespace Snookering.Core.Mathematics;

/// <summary>
/// Double-precision 2D vector for the table-plane simulation.
/// Deterministic: scalar double math only, no SIMD, no trig.
/// </summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly double X;
    public readonly double Y;

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vec2 Zero = new(0.0, 0.0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Dot(Vec2 other) => X * other.X + Y * other.Y;

    /// <summary>Z-component of the 3D cross product (a.k.a. the 2D "perp dot").</summary>
    public double Cross(Vec2 other) => X * other.Y - Y * other.X;

    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Counter-clockwise perpendicular.</summary>
    public Vec2 Perp => new(-Y, X);

    /// <summary>Unit vector; returns Zero for the zero vector.</summary>
    public Vec2 Normalized()
    {
        var lenSq = LengthSquared;
        if (lenSq <= 0.0)
            return Zero;
        var inv = 1.0 / Math.Sqrt(lenSq);
        return new Vec2(X * inv, Y * inv);
    }

    public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
    public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

    public override string ToString() => $"({X:R}, {Y:R})";
}
