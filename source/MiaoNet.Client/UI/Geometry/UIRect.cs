using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// an axis-aligned rectangle in logical screen units
public readonly record struct UIRect(float X, float Y, float Width, float Height)
{
    public static readonly UIRect Empty = new(0f, 0f, 0f, 0f);

    public float Left => X;

    public float Top => Y;

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public UIOffset Position => new(X, Y);

    public UISize Size => new(Width, Height);

    public bool Contains(UIOffset point)
        => point.X >= X && point.X < X + Width
        && point.Y >= Y && point.Y < Y + Height;

    // true when the two overlap; touching edges don't count
    public bool Intersects(UIRect other)
        => other.X < Right && X < other.Right
        && other.Y < Bottom && Y < other.Bottom;

    public UIRect WithOffset(UIOffset offset)
        => new(X + offset.X, Y + offset.Y, Width, Height);

    // shrink by insets, never below a zero extent
    public UIRect Deflate(EdgeInsets insets)
        => new(
            X + insets.Left,
            Y + insets.Top,
            MathF.Max(0f, Width - insets.Horizontal),
            MathF.Max(0f, Height - insets.Vertical));

    public UIRect Inflate(EdgeInsets insets)
        => new(
            X - insets.Left,
            Y - insets.Top,
            Width + insets.Horizontal,
            Height + insets.Vertical);

    // snap to whole pixels: floor the origin, then derive the extent from the floored far
    // edge so adjacent rects tile without seams
    public UIRect Snap()
    {
        float snappedX = MathF.Floor(X);
        float snappedY = MathF.Floor(Y);
        return new UIRect(
            snappedX,
            snappedY,
            MathF.Floor(X + Width) - snappedX,
            MathF.Floor(Y + Height) - snappedY);
    }
}
