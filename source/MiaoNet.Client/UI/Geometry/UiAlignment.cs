using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// nine-way alignment, used by AlignNode and StackNode
public enum UiAlignment
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
    public static float HorizontalFactor(this UiAlignment alignment) => alignment switch
    {
        UiAlignment.TopLeft or UiAlignment.CenterLeft or UiAlignment.BottomLeft => 0f,
        UiAlignment.TopCenter or UiAlignment.Center or UiAlignment.BottomCenter => 0.5f,
        _ => 1f,
    };

    // 0 = top, 0.5 = middle, 1 = bottom
    public static float VerticalFactor(this UiAlignment alignment) => alignment switch
    {
        UiAlignment.TopLeft or UiAlignment.TopCenter or UiAlignment.TopRight => 0f,
        UiAlignment.CenterLeft or UiAlignment.Center or UiAlignment.CenterRight => 0.5f,
        _ => 1f,
    };

    public static UiOffset OffsetFor(this UiAlignment alignment, UiSize container, UiSize child)
        => new(
            (container.Width - child.Width) * alignment.HorizontalFactor(),
            (container.Height - child.Height) * alignment.VerticalFactor());
}
