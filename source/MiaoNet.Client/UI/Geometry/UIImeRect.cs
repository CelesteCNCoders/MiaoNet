using System;

namespace Celeste.Mod.MiaoNet.UI.Geometry;

// maps the ime composition anchor onto the integer backbuffer rect the platform's text-input api
// wants, so the candidate window points at the composition instead of wherever it defaults to.
//
// kept next to UIScissor and free of XNA for the same reason: the arithmetic is the part that can
// be unit tested, and a wrong rect is invisible until someone uses a cjk ime.
public static class UIImeRect
{
    // anchorX/anchorY/width/height are logical screen coords; xScale/yScale and the viewport come
    // from the render target. the width never collapses to zero: a 0-wide rect makes some backends
    // fall back to the default position.
    public static UiPixelRect Map(
        float anchorX,
        float anchorY,
        float width,
        float height,
        float xScale,
        float yScale,
        int viewportX,
        int viewportY)
    {
        return new UiPixelRect(
            (int)(viewportX + (anchorX * xScale)),
            (int)(viewportY + (anchorY * yScale)),
            Math.Max(1, (int)(width * xScale)),
            (int)(height * yScale));
    }
}
