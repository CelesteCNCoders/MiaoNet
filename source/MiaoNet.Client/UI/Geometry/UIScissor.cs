using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// maps a logical rect onto backbuffer scissor coordinates.
// kept as its own function rather than inlined into MiaoNetUICanvas so the arithmetic fed to
// GraphicsDevice.ScissorRectangle can be unit tested: getting it wrong silently clips the ui,
// and it's the one part of the render path you can't really check in-game (the player list
// viewport is the whole screen anyway).
public static class UIScissor
{
    // smallest integer rect covering the transformed rect, clamped to the viewport;
    // a rect fully outside comes back as zero-area
    public static Rectangle Map(UIRect rect, Matrix transform, int viewportWidth, int viewportHeight)
    {
        Vector2 a = Vector2.Transform(new Vector2(rect.X, rect.Y), transform);
        Vector2 b = Vector2.Transform(new Vector2(rect.Right, rect.Bottom), transform);

        float left = MathF.Min(a.X, b.X);
        float top = MathF.Min(a.Y, b.Y);
        float right = MathF.Max(a.X, b.X);
        float bottom = MathF.Max(a.Y, b.Y);

        int x = ClampToViewport((int)MathF.Floor(left), viewportWidth);
        int y = ClampToViewport((int)MathF.Floor(top), viewportHeight);
        int far = ClampToViewport((int)MathF.Ceiling(right), viewportWidth);
        int near = ClampToViewport((int)MathF.Ceiling(bottom), viewportHeight);

        return new Rectangle(x, y, Math.Max(0, far - x), Math.Max(0, near - y));
    }

    private static int ClampToViewport(int value, int extent)
        => Math.Clamp(value, 0, Math.Max(0, extent));
}
