using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// a width/height pair in logical screen units (Engine.Width / Engine.Height).
// no XNA types here so the layout core can be unit tested without Celeste.
public readonly record struct UISize(float Width, float Height)
{
    public static readonly UISize Zero = new(0f, 0f);

    public static UISize operator +(UISize a, UISize b)
        => new(a.Width + b.Width, a.Height + b.Height);

    public static UISize operator -(UISize a, UISize b)
        => new(a.Width - b.Width, a.Height - b.Height);

    public UISize Max(UISize other)
        => new(MathF.Max(Width, other.Width), MathF.Max(Height, other.Height));
}
