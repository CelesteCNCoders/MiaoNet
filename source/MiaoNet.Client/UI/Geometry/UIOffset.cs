using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// a point or translation in logical screen units
public readonly record struct UIOffset(float X, float Y)
{
    public static readonly UIOffset Zero = new(0f, 0f);

    public static UIOffset operator +(UIOffset a, UIOffset b)
        => new(a.X + b.X, a.Y + b.Y);

    public static UIOffset operator -(UIOffset a, UIOffset b)
        => new(a.X - b.X, a.Y - b.Y);

    public static UIOffset operator *(UIOffset a, float factor)
        => new(a.X * factor, a.Y * factor);
}
