using System.Runtime.CompilerServices;

namespace Vivarium.Sim.Core;

/// <summary>Horizontal-plane vector (world X, Z). Double precision for deterministic sim math.</summary>
public readonly record struct Vec2(double X, double Z)
{
    public static readonly Vec2 Zero = new(0, 0);
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Z);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Z * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Z * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Z / s);
    public double Dot(Vec2 o) => X * o.X + Z * o.Z;
    public double Cross(Vec2 o) => X * o.Z - Z * o.X;
    public double LengthSq => X * X + Z * Z;
    public double Length => Math.Sqrt(LengthSq);
    public Vec2 Normalized() { double l = Length; return l > 1e-12 ? this / l : Zero; }
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static double DistanceSq(Vec2 a, Vec2 b) => (a - b).LengthSq;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;
    public static Vec2 FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));
    public double Angle => Math.Atan2(Z, X);
    public Vec2 Rotated(double r) { double c = Math.Cos(r), s = Math.Sin(r); return new(X * c - Z * s, X * s + Z * c); }
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Z);
    public override string ToString() => $"({X:0.###}, {Z:0.###})";
}

/// <summary>3D vector, Y up (matches Godot).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Up = new(0, 1, 0);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double LengthSq => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSq);
    public Vec3 Normalized() { double l = Length; return l > 1e-12 ? this / l : Zero; }
    public Vec2 XZ => new(X, Z);
    public static Vec3 FromXZ(Vec2 p, double y) => new(p.X, y, p.Z);
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}

public static class MathD
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    public static double SmoothStep(double e0, double e1, double x) { double t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
    /// <summary>Gaussian-shaped preference: 1 at optimum, falls with tolerance (σ).</summary>
    public static double Preference(double value, double optimum, double tolerance) =>
        tolerance <= 0 ? (Math.Abs(value - optimum) < 1e-9 ? 1 : 0) : Math.Exp(-0.5 * Math.Pow((value - optimum) / tolerance, 2));
    public static double WrapAngle(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }
    /// <summary>Returns v if finite, otherwise fallback. Guards numeric corruption at system boundaries.</summary>
    public static double Sane(double v, double fallback = 0) => double.IsFinite(v) ? v : fallback;
}
