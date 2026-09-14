using Celeste.Mod.MiaoNet.UI.Geometry;
using Celeste.Mod.MiaoNet.UI.Styling;

namespace Celeste.Mod.MiaoNet.UI.Rendering;

// IUiTexture over a Monocle texture, for the avatar-less status icons such as paused,
// live mode, taking golden and the debug map marker.
public sealed class MiaoNetTexture : IUITexture
{
    private readonly MTexture texture;

    public MiaoNetTexture(MTexture texture) => this.texture = texture;

    public float Width => texture.Width;

    public float Height => texture.Height;

    public void Draw(
        IUICanvas canvas,
        UIOffset position,
        UIColor tint,
        float scale,
        HorizontalAnchor horizontalAnchor = HorizontalAnchor.Left,
        VerticalAnchor verticalAnchor = VerticalAnchor.Top)
    {
        var origin = new Vector2(
            texture.Width * horizontalAnchor.HorizontalFactor(),
            texture.Height * verticalAnchor.VerticalFactor());
        texture.Draw(position.ToVector2(), origin, tint.ToXna(), new Vector2(scale));
    }
}
