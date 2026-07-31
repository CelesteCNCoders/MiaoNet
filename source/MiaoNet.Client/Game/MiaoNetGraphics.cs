using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public static class MiaoNetGraphics
{
    public static Effect RadialAlphaMaskEffect { get; private set; } = null!;

    public static void LoadContent()
    {
        var asset = Everest.Content.Get("Effects/MiaoNet/RadialAlphaMask.cso")
            ?? throw new KeyNotFoundException("RadialAlphaMask.cso is not found.");
        RadialAlphaMaskEffect = new Effect(Engine.Graphics.GraphicsDevice, asset.Data);
    }
}
