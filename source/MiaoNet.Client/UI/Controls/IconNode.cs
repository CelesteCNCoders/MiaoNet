using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Controls;

// draws an icon texture. TargetHeight scales it to exactly that height, keeping the aspect
// ratio -- that is what the old player list did to every status icon.
public sealed class IconNode : UINode
{
    private IUITexture? texture;
    private float targetHeight;

    public IUITexture? Texture
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

    public UIColor Tint { get; set; } = UIColor.White;

    // extra paint-only offset, used by the floating paused icon animation
    public UIOffset PaintOffset { get; set; }

    private float Scale => texture is null || targetHeight <= 0f ? 1f : targetHeight / texture.Height;

    protected override UISize OnMeasure(BoxConstraints constraints)
    {
        if (texture is null)
        {
            return constraints.Constrain(UISize.Zero);
        }

        float scale = Scale;
        return constraints.Constrain(new UISize(texture.Width * scale, texture.Height * scale));
    }

    protected override void PaintSelf(IUICanvas canvas, float opacity)
    {
        if (texture is null)
        {
            return;
        }

        texture.Draw(
            canvas,
            new UIOffset(Bounds.X + PaintOffset.X, Bounds.Y + PaintOffset.Y),
            Tint * opacity,
            Scale);
    }
}
