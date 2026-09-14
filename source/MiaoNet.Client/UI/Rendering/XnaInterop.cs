using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// converts between the XNA-free ui value types and the game's drawing types. the only place
// allowed to know both worlds.
public static class XnaInterop
{
    public static Color ToXna(this UiColor color)
        => new(
            ToByte(color.R),
            ToByte(color.G),
            ToByte(color.B),
            ToByte(color.A));

    public static Vector2 ToVector2(this UiOffset offset) => new(offset.X, offset.Y);

    public static Rectangle ToRectangle(this UiRect rect)
        => new(
            (int)MathF.Floor(rect.X),
            (int)MathF.Floor(rect.Y),
            (int)MathF.Ceiling(rect.Width),
            (int)MathF.Ceiling(rect.Height));

    // anchor factors for the font's justify vector.
    public static Vector2 ToJustify(this HorizontalAnchor horizontal, VerticalAnchor vertical)
        => new(HorizontalFactor(horizontal), VerticalFactor(vertical));

    public static float HorizontalFactor(this HorizontalAnchor anchor) => anchor switch
    {
        HorizontalAnchor.Left => 0f,
        HorizontalAnchor.Center => 0.5f,
        _ => 1f,
    };

    public static float VerticalFactor(this VerticalAnchor anchor) => anchor switch
    {
        VerticalAnchor.Top => 0f,
        VerticalAnchor.Center => 0.5f,
        _ => 1f,
    };

    private static byte ToByte(float component)
        => (byte)Math.Clamp((int)MathF.Round(component * 255f), 0, 255);
}
