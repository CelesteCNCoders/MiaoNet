using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// a linear RGBA color with components in [0, 1].
// kept independent of XNA's Color and MiaoNet.Shared.Color so the layout/style core stays
// XNA-free and testable; the renderer converts it at the boundary.
public readonly record struct UIColor(float R, float G, float B, float A)
{
    public static readonly UIColor Transparent = new(0f, 0f, 0f, 0f);

    public static readonly UIColor Black = new(0f, 0f, 0f, 1f);

    public static readonly UIColor White = new(1f, 1f, 1f, 1f);

    public static readonly UIColor Gray = FromBytes(128, 128, 128);

    public static readonly UIColor LightGray = FromBytes(211, 211, 211);

    public static readonly UIColor CornflowerBlue = FromBytes(100, 149, 237);

    public static readonly UIColor Cyan = FromBytes(0, 255, 255);

    public static readonly UIColor Yellow = FromBytes(255, 255, 0);

    public static readonly UIColor Wheat = FromBytes(245, 222, 179);

    public static UIColor FromBytes(byte r, byte g, byte b, byte a = 255)
        => new(r / 255f, g / 255f, b / 255f, a / 255f);

    // scales all four channels, matching XNA's Color * float. every dim or fade relies on
    // that: White * 0.5f is mid grey at half alpha, while scaling alpha alone would stay
    // full-bright white, which looks wrong over the game world.
    // same for the completion highlight (Wheat * (0x22 / 255)) and every fade.
    public static UIColor operator *(UIColor color, float factor)
        => new(color.R * factor, color.G * factor, color.B * factor, color.A * factor);

    public static UIColor Lerp(UIColor from, UIColor to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new(
            from.R + ((to.R - from.R) * t),
            from.G + ((to.G - from.G) * t),
            from.B + ((to.B - from.B) * t),
            from.A + ((to.A - from.A) * t));
    }
}
