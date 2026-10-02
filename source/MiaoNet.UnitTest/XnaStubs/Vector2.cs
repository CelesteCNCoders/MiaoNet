namespace Microsoft.Xna.Framework;

// Stand-in for XNA's Vector2, see Color.cs. The layout code does its arithmetic on these, so the
// operators have to behave exactly like the real thing.
public struct Vector2 : IEquatable<Vector2>
{
    public float X;

    public float Y;

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public Vector2(float value)
    {
        X = value;
        Y = value;
    }

    public static readonly Vector2 Zero = new(0f, 0f);

    public static readonly Vector2 One = new(1f, 1f);

    public static readonly Vector2 UnitX = new(1f, 0f);

    public static readonly Vector2 UnitY = new(0f, 1f);

    public readonly float Length() => MathF.Sqrt((X * X) + (Y * Y));

    public readonly float LengthSquared() => (X * X) + (Y * Y);

    public static float Distance(Vector2 value1, Vector2 value2)
    {
        float dx = value1.X - value2.X;
        float dy = value1.Y - value2.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    public static float Dot(Vector2 value1, Vector2 value2) => (value1.X * value2.X) + (value1.Y * value2.Y);

    public void Normalize()
    {
        float length = Length();
        if (length > 0f)
        {
            X /= length;
            Y /= length;
        }
    }

    public static Vector2 Normalize(Vector2 value)
    {
        value.Normalize();
        return value;
    }

    public static Vector2 Transform(Vector2 position, Matrix matrix)
        => new(
            (position.X * matrix.M11) + (position.Y * matrix.M21) + matrix.M41,
            (position.X * matrix.M12) + (position.Y * matrix.M22) + matrix.M42);

    public static Vector2 operator +(Vector2 value1, Vector2 value2) => new(value1.X + value2.X, value1.Y + value2.Y);

    public static Vector2 operator -(Vector2 value1, Vector2 value2) => new(value1.X - value2.X, value1.Y - value2.Y);

    public static Vector2 operator -(Vector2 value) => new(-value.X, -value.Y);

    public static Vector2 operator *(Vector2 value, float scale) => new(value.X * scale, value.Y * scale);

    public static Vector2 operator *(float scale, Vector2 value) => new(value.X * scale, value.Y * scale);

    public static Vector2 operator /(Vector2 value, float divisor) => new(value.X / divisor, value.Y / divisor);

    public readonly bool Equals(Vector2 other) => X == other.X && Y == other.Y;

    public readonly override bool Equals(object? obj) => obj is Vector2 other && Equals(other);

    public readonly override int GetHashCode() => HashCode.Combine(X, Y);

    public readonly override string ToString() => $"{{X:{X} Y:{Y}}}";

    public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);

    public static bool operator !=(Vector2 left, Vector2 right) => !left.Equals(right);
}
