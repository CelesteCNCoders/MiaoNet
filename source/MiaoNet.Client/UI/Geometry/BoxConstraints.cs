using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// constraints passed down the tree: min and max extent on each axis.
// "constraints down, sizes up" — a node measures itself inside these bounds and its
// parent decides where it finally goes.
public readonly record struct BoxConstraints(float MinWidth, float MaxWidth, float MinHeight, float MaxHeight)
{
    public static readonly BoxConstraints Unbounded
        = new(0f, float.PositiveInfinity, 0f, float.PositiveInfinity);

    public static BoxConstraints Tight(float width, float height)
        => new(width, width, height, height);

    public static BoxConstraints Tight(UiSize size)
        => new(size.Width, size.Width, size.Height, size.Height);

    public static BoxConstraints Loose(float width, float height)
        => new(0f, width, 0f, height);

    public static BoxConstraints Loose(UiSize size)
        => new(0f, size.Width, 0f, size.Height);

    public bool IsTight => MinWidth == MaxWidth && MinHeight == MaxHeight;

    public UiSize Constrain(UiSize size)
        => new(Clamp(size.Width, MinWidth, MaxWidth), Clamp(size.Height, MinHeight, MaxHeight));

    public BoxConstraints Deflate(EdgeInsets insets)
        => new(
            MathF.Max(0f, MinWidth - insets.Horizontal),
            MathF.Max(0f, MaxWidth - insets.Horizontal),
            MathF.Max(0f, MinHeight - insets.Vertical),
            MathF.Max(0f, MaxHeight - insets.Vertical));

    // drop the mins so a child can shrink to its own size
    public BoxConstraints Loosen()
        => new(0f, MaxWidth, 0f, MaxHeight);

    // replace the width bounds, but keep min <= max
    public BoxConstraints WithWidth(float minWidth, float maxWidth)
        => new(minWidth, MathF.Max(minWidth, maxWidth), MinHeight, MaxHeight);

    // replace the height bounds, but keep min <= max
    public BoxConstraints WithHeight(float minHeight, float maxHeight)
        => new(MinWidth, MaxWidth, minHeight, MathF.Max(minHeight, maxHeight));

    public BoxConstraints TightenWidth(float width) => WithWidth(width, width);

    public BoxConstraints TightenHeight(float height) => WithHeight(height, height);

    // infinity-safe clamp that also tolerates a reversed range
    private static float Clamp(float value, float min, float max)
    {
        if (min > max)
        {
            (min, max) = (max, min);
        }
        if (value < min)
        {
            return min;
        }
        return value > max ? max : value;
    }
}
