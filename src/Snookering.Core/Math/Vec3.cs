using System;

namespace Snookering.Core.Mathematics;

/// <summary>
/// Double-precision 3D vector, used for angular velocity (X/Y horizontal spin, Z vertical english).
/// Deterministic: scalar double math only.
/// </summary>
public readonly struct Vec3 : IEquatable<Vec3>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Vec3 Zero = new(0.0, 0.0, 0.0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);

    public double Dot(Vec3 other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vec3 Cross(Vec3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>The horizontal (table-plane) components.</summary>
    public Vec2 Xy => new(X, Y);

    public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vec3 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
    public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

    public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
}
