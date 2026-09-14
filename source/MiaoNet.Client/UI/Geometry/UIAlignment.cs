using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// nine-way alignment, used by AlignNode and StackNode
public enum UIAlignment
{
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public static class UiAlignmentExtensions
{
    // 0 = left, 0.5 = center, 1 = right
    public static float HorizontalFactor(this UIAlignment alignment) => alignment switch
    {
        UIAlignment.TopLeft or UIAlignment.CenterLeft or UIAlignment.BottomLeft => 0f,
        UIAlignment.TopCenter or UIAlignment.Center or UIAlignment.BottomCenter => 0.5f,
        _ => 1f,
    };

    // 0 = top, 0.5 = middle, 1 = bottom
    public static float VerticalFactor(this UIAlignment alignment) => alignment switch
    {
        UIAlignment.TopLeft or UIAlignment.TopCenter or UIAlignment.TopRight => 0f,
        UIAlignment.CenterLeft or UIAlignment.Center or UIAlignment.CenterRight => 0.5f,
        _ => 1f,
    };

    public static UIOffset OffsetFor(this UIAlignment alignment, UISize container, UISize child)
        => new(
            (container.Width - child.Width) * alignment.HorizontalFactor(),
            (container.Height - child.Height) * alignment.VerticalFactor());
}
