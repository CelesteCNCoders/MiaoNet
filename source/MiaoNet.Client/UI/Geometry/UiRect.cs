using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// an axis-aligned rectangle in logical screen units
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public static readonly UiRect Empty = new(0f, 0f, 0f, 0f);

    public float Left => X;

    public float Top => Y;

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public UiOffset Position => new(X, Y);

    public UiSize Size => new(Width, Height);

    public bool Contains(UiOffset point)
        => point.X >= X && point.X < X + Width
        && point.Y >= Y && point.Y < Y + Height;

    // true when the two overlap; touching edges don't count
    public bool Intersects(UiRect other)
        => other.X < Right && X < other.Right
        && other.Y < Bottom && Y < other.Bottom;

    public UiRect WithOffset(UiOffset offset)
        => new(X + offset.X, Y + offset.Y, Width, Height);

    // shrink by insets, never below a zero extent
    public UiRect Deflate(EdgeInsets insets)
        => new(
            X + insets.Left,
            Y + insets.Top,
            MathF.Max(0f, Width - insets.Horizontal),
            MathF.Max(0f, Height - insets.Vertical));

    public UiRect Inflate(EdgeInsets insets)
        => new(
            X - insets.Left,
            Y - insets.Top,
            Width + insets.Horizontal,
            Height + insets.Vertical);

    // snap to whole pixels: floor the origin, then derive the extent from the floored far
    // edge so adjacent rects tile without seams
    public UiRect Snap()
    {
        float snappedX = MathF.Floor(X);
        float snappedY = MathF.Floor(Y);
        return new UiRect(
            snappedX,
            snappedY,
            MathF.Floor(X + Width) - snappedX,
            MathF.Floor(Y + Height) - snappedY);
    }
}
