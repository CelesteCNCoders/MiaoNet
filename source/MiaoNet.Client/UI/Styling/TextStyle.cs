using System;
using Celeste.Mod.MiaoNet.UI.Geometry;

namespace Celeste.Mod.MiaoNet.UI.Styling;

// text decorations the renderer abstraction understands.
[Flags]
public enum TextDecoration
{
    None = 0,
    Outline = 1 << 0,
    Underline = 1 << 1,
    Strikethrough = 1 << 2,
}

// horizontal anchor; becomes the x component of the renderer's justify vector.
public enum HorizontalAnchor
{
    Left,
    Center,
    Right,
}

// vertical anchor; becomes the y component of the renderer's justify vector.
public enum VerticalAnchor
{
    Top,
    Center,
    Bottom,
}

public static class AnchorExtensions
{
    // 0 = left, 0.5 = center, 1 = right
    public static float HorizontalFactor(this HorizontalAnchor anchor) => anchor switch
    {
        HorizontalAnchor.Left => 0f,
        HorizontalAnchor.Center => 0.5f,
        _ => 1f,
    };

    // 0 = top, 0.5 = middle, 1 = bottom
    public static float VerticalFactor(this VerticalAnchor anchor) => anchor switch
    {
        VerticalAnchor.Top => 0f,
        VerticalAnchor.Center => 0.5f,
        _ => 1f,
    };
}

// per-text drawing attributes; immutable so it can be shared and combined with `with`.
// the actual text backend comes from UIStyle.TextRenderer.
public sealed record TextStyle
{
    public static readonly TextStyle Default = new();

    public Color? Color { get; init; }

    public float Scale { get; init; } = 1f;

    public float? LineHeight { get; init; }

    public TextDecoration Decorations { get; init; } = TextDecoration.None;

    public HorizontalAnchor HorizontalAnchor { get; init; } = HorizontalAnchor.Left;

    public VerticalAnchor VerticalAnchor { get; init; } = VerticalAnchor.Top;

    public float EffectiveLineHeight => LineHeight ?? 0f;
}
