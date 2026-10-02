namespace Microsoft.Xna.Framework;

// Stand-in for XNA's Rectangle, see Color.cs. Integer pixels, same as the real type: the ui's own
// UIRect stays float and logical, this is only what the platform/scissor calls want.
public struct Rectangle : IEquatable<Rectangle>
{
    public int X;

    public int Y;

    public int Width;

    public int Height;

    public Rectangle(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static readonly Rectangle Empty = new(0, 0, 0, 0);

    public readonly int Left => X;

    public readonly int Top => Y;

    public readonly int Right => X + Width;

    public readonly int Bottom => Y + Height;

    public readonly Vector2 Location => new(X, Y);

    public readonly Vector2 Center => new(X + (Width / 2), Y + (Height / 2));

    public readonly bool Contains(int x, int y) => X <= x && x < Right && Y <= y && y < Bottom;

    public readonly bool Contains(Vector2 value) => Contains((int)value.X, (int)value.Y);

    public readonly bool Intersects(Rectangle value)
        => value.Left < Right && Left < value.Right && value.Top < Bottom && Top < value.Bottom;

    public readonly bool Equals(Rectangle other)
        => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public readonly override bool Equals(object? obj) => obj is Rectangle other && Equals(other);

    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public readonly override string ToString() => $"{{X:{X} Y:{Y} Width:{Width} Height:{Height}}}";

    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);

    public static bool operator !=(Rectangle left, Rectangle right) => !left.Equals(right);
}
