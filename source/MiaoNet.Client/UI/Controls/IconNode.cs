using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// draws an icon texture. TargetHeight scales it to exactly that height, keeping the aspect
// ratio -- that is what the old player list did to every status icon.
public sealed class IconNode : UiNode
{
    private IUiTexture? texture;
    private float targetHeight;

    public IUiTexture? Texture
    {
        get => texture;
        set
        {
            if (ReferenceEquals(texture, value))
            {
                return;
            }

            texture = value;
            InvalidateMeasure();
        }
    }

    public float TargetHeight
    {
        get => targetHeight;
        set
        {
            if (targetHeight == value)
            {
                return;
            }

            targetHeight = value;
            InvalidateMeasure();
        }
    }

    public UiColor Tint { get; set; } = UiColor.White;

    // extra paint-only offset, used by the floating paused icon animation
    public UiOffset PaintOffset { get; set; }

    private float Scale => texture is null || targetHeight <= 0f ? 1f : targetHeight / texture.Height;

    protected override UiSize OnMeasure(BoxConstraints constraints)
    {
        if (texture is null)
        {
            return constraints.Constrain(UiSize.Zero);
        }

        float scale = Scale;
        return constraints.Constrain(new UiSize(texture.Width * scale, texture.Height * scale));
    }

    protected override void PaintSelf(IUiCanvas canvas, float opacity)
    {
        if (texture is null)
        {
            return;
        }

        texture.Draw(
            canvas,
            new UiOffset(Bounds.X + PaintOffset.X, Bounds.Y + PaintOffset.Y),
            Tint * opacity,
            Scale);
    }
}
