using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// per-edge insets in logical screen units, for padding and clipping
public readonly record struct EdgeInsets(float Left, float Top, float Right, float Bottom)
{
    public static readonly EdgeInsets Zero = new(0f, 0f, 0f, 0f);

    public EdgeInsets(float all)
        : this(all, all, all, all)
    {
    }

    public EdgeInsets(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;
}
