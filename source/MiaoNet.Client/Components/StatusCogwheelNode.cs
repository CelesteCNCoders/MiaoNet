using System;
using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Rendering;
using Celeste.Mod.MiaoNet.UI.Scene;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

// the status cogwheel, a rotating sprite with a hand-drawn outline.
// sits outside the XNA-free UI core on purpose: the outline uses Draw.SpriteBatch and Monocle's
// MTexture internals (ScaleFix, ClipRect, Center, DrawOffset) that the core doesn't expose, and
// exposing them would leak Monocle into something meant to be testable without the game.
internal sealed class StatusCogwheelNode : UINode
{
    // the scale this node draws the icon at
    public const float IconScale = 1f / 3.5f;

    private readonly MTexture texture;

    public StatusCogwheelNode(MTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        this.texture = texture;
    }

    public float Rotation { get; set; }

    public Color Tint { get; set; } = Color.White;

    protected override UISize OnMeasure(BoxConstraints constraints)
        => constraints.Constrain(new UISize(texture.Width * IconScale, texture.Height * IconScale));

    protected override void PaintSelf(IUICanvas canvas, float opacity)
        => DrawOutlineCentered(
            texture,
            new Vector2(Bounds.X + (Bounds.Width * 0.5f), Bounds.Y + (Bounds.Height * 0.5f)),
            Tint * opacity,
            IconScale,
            Rotation);

    // eight offset outline passes plus the body
    private static void DrawOutlineCentered(MTexture texture, Vector2 position, Color color, float scale, float rotation)
    {
        float scaleFix = texture.ScaleFix;
        scale *= scaleFix;
        Rectangle clipRect = texture.ClipRect;
        Vector2 origin = (texture.Center - texture.DrawOffset) / scaleFix;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                if (i != 0 || j != 0)
                {
                    float alpha = color.A / 255f;
                    Draw.SpriteBatch.Draw(
                        texture.Texture.Texture_Safe,
                        position + new Vector2(i, j),
                        clipRect,
                        Color.Black * MathF.Pow(alpha, 3f), // diff from original DrawOutlineCentered
                        rotation,
                        origin,
                        scale,
                        SpriteEffects.None,
                        0f);
                }
            }
        }

        Draw.SpriteBatch.Draw(texture.Texture.Texture_Safe, position, clipRect, color, rotation, origin, scale, SpriteEffects.None, 0f);
    }
}
