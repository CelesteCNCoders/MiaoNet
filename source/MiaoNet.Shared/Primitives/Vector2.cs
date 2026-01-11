using System.Diagnostics;

namespace MiaoNet.Shared;

[DebuggerDisplay("{DebuggerDisplay}")]
public struct Vector2 : IEquatable<Vector2>
{
    public float X;
    public float Y;

    public Vector2(float x, float y) { X = x; Y = y; }

    public Vector2(float value) { X = value; Y = value; }

    public override readonly string ToString() => $"{{X:{X} Y:{Y}}}";

    public readonly bool Equals(Vector2 other)
        => X == other.X && Y == other.Y;

    public override readonly bool Equals(object? obj) => obj is Vector2 other && Equals(other);

    public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);

    public static bool operator !=(Vector2 left, Vector2 right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    // Add more things if you need

    private readonly string DebuggerDisplay => $"{X} {Y}";
}
