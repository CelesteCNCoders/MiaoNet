using System;
using System.Collections.Generic;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// IUiCanvas over Monocle's sprite batch. we draw through Draw.Rect and Draw.Line so it shares
// the batch the host already started, and clip with the graphics device scissor rectangle.
// the host has to call BeginFrame before painting and EndFrame after, inside an active
// Draw.SpriteBatch.Begin / End pair.
// scissor state is captured when a batch begins, so a clip change restarts the batch with the
// same state the host used, only swapping the rasterizer state.
public sealed class MiaoNetUICanvas : IUICanvas
{
    private static readonly RasterizerState ClippedRasterizer = new() { ScissorTestEnable = true };
    private static readonly RasterizerState UnclippedRasterizer = RasterizerState.CullNone;

    private readonly List<Rectangle> clipStack = [];
    private Rectangle? currentClip;
    private bool frameActive;

    // whether a frame is in progress and the sprite batch is available.
    public bool IsActive => frameActive;

    public void BeginFrame()
    {
        frameActive = true;
        clipStack.Clear();
        currentClip = null;
    }

    public void EndFrame()
    {
        frameActive = false;
        clipStack.Clear();
        currentClip = null;
    }

    public void FillRect(UIRect rect, UIColor color)
        => Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, color.ToXna());

    public void DrawLine(UIOffset from, UIOffset to, UIColor color, float thickness)
        => Draw.Line(from.ToVector2(), to.ToVector2(), color.ToXna(), thickness);

    public void PushClip(UIRect rect)
    {
        Rectangle requested = ToScissor(rect);
        Rectangle clipped = currentClip is { } parent ? Rectangle.Intersect(parent, requested) : requested;
        clipStack.Add(clipped);
        currentClip = clipped;
        ApplyClip();
    }

    public void PopClip()
    {
        if (clipStack.Count == 0)
        {
            return;
        }

        clipStack.RemoveAt(clipStack.Count - 1);
        currentClip = clipStack.Count > 0 ? clipStack[^1] : null;
        ApplyClip();
    }

    // maps a logical rect to backbuffer scissor coords. the arithmetic lives in UiScissor so it
    // can be unit tested without XNA.
    private static Rectangle ToScissor(UIRect rect)
    {
        Matrix matrix = Engine.ScreenMatrix;
        var transform = new UiTransform2D(
            matrix.M11, matrix.M12,
            matrix.M21, matrix.M22,
            matrix.M41, matrix.M42);

        Viewport viewport = Engine.Graphics.GraphicsDevice.Viewport;
        UiPixelRect mapped = UIScissor.Map(rect, transform, viewport.Width, viewport.Height);
        return new Rectangle(mapped.X, mapped.Y, mapped.Width, mapped.Height);
    }

    private void ApplyClip()
    {
        if (!frameActive)
        {
            return;
        }

        // Scissor test is part of the rasterizer state captured at Begin, so the batch has to
        // be restarted. End/Begin net out, leaving the batch active for the host's End.
        Draw.SpriteBatch.End();

        GraphicsDevice device = Engine.Graphics.GraphicsDevice;
        if (currentClip is { } clip)
        {
            device.ScissorRectangle = clip;
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                ClippedRasterizer,
                null,
                Engine.ScreenMatrix);
        }
        else
        {
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                UnclippedRasterizer,
                null,
                Engine.ScreenMatrix);
        }
    }
}
