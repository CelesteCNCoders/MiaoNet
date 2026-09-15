using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// a 2d affine transform laid out like XNA's Matrix on the XY plane, so the scissor math
// can be expressed and tested without XNA.
public readonly record struct UiTransform2D(
    float M11,
    float M12,
    float M21,
    float M22,
    float M41,
    float M42)
{
    public static readonly UiTransform2D Identity = new(1f, 0f, 0f, 1f, 0f, 0f);

    // same math as Vector2.Transform
    public UIOffset Transform(UIOffset point)
        => new(
            (point.X * M11) + (point.Y * M21) + M41,
            (point.X * M12) + (point.Y * M22) + M42);
}

// integer pixel bounds produced by UIScissor.Map
public readonly record struct UiPixelRect(int X, int Y, int Width, int Height);

// maps a logical rect onto backbuffer scissor coordinates.
// separated from MiaoNetUICanvas so the arithmetic fed to GraphicsDevice.ScissorRectangle can
// be unit tested: getting it wrong silently clips the ui, and it's the one part of the render
// path you can't really check in-game (the player list viewport is the whole screen anyway).
public static class UIScissor
{
    // smallest integer rect covering the transformed rect, clamped to the viewport;
    // a rect fully outside comes back as zero-area
    public static UiPixelRect Map(UIRect rect, UiTransform2D transform, int viewportWidth, int viewportHeight)
    {
        UIOffset a = transform.Transform(new UIOffset(rect.X, rect.Y));
        UIOffset b = transform.Transform(new UIOffset(rect.Right, rect.Bottom));

        float left = MathF.Min(a.X, b.X);
        float top = MathF.Min(a.Y, b.Y);
        float right = MathF.Max(a.X, b.X);
        float bottom = MathF.Max(a.Y, b.Y);

        int x = ClampToViewport((int)MathF.Floor(left), viewportWidth);
        int y = ClampToViewport((int)MathF.Floor(top), viewportHeight);
        int far = ClampToViewport((int)MathF.Ceiling(right), viewportWidth);
        int near = ClampToViewport((int)MathF.Ceiling(bottom), viewportHeight);

        return new UiPixelRect(x, y, Math.Max(0, far - x), Math.Max(0, near - y));
    }

    private static int ClampToViewport(int value, int extent)
        => Math.Clamp(value, 0, Math.Max(0, extent));
}
