using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// a point or translation in logical screen units
public readonly record struct UiOffset(float X, float Y)
{
    public static readonly UiOffset Zero = new(0f, 0f);

    public static UiOffset operator +(UiOffset a, UiOffset b)
        => new(a.X + b.X, a.Y + b.Y);

    public static UiOffset operator -(UiOffset a, UiOffset b)
        => new(a.X - b.X, a.Y - b.Y);

    public static UiOffset operator *(UiOffset a, float factor)
        => new(a.X * factor, a.Y * factor);
}
