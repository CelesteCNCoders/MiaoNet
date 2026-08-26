using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed class GhostRenderLayerEntity : MiaoNetEntity
{
    private readonly bool isHigh;

    public GhostRenderLayerEntity(bool isHigh)
    {
        Tag = MiaoNetTag.Normal;
        Depth = isHigh ? Depths.Top : (Depths.Player + 1);
        this.isHigh = isHigh;
    }

    public override void Render()
    {
        Level level = SceneAs<Level>();

        if (level.Tracker.GetEntities<MiaoNetGhostEntity>().Count == 0)
            return;

        var gd = Engine.Instance.GraphicsDevice;
        var settings = MiaoNetModule.Settings;

        GameplayRenderer.End();

        // draw all ghost entities without alpha set
        gd.SetRenderTarget(GameplayBuffers.TempA);
        gd.Clear(Color.Transparent);

        GameplayRenderer.Begin();
        foreach (MiaoNetGhostEntity entity in level.Tracker.GetEntities<MiaoNetGhostEntity>().Cast<MiaoNetGhostEntity>())
        {
            if ((isHigh ? entity.Depth <= Depth : entity.Depth >= Depth) && entity.Visible)
                entity.GhostRender();
        }
        GameplayRenderer.End();

        // prepare effect if needed
        Effect? effect = null;
        if (settings.DistanceBasedOpacity && !MiaoNetModule.Instance.MiaoNetContext.MainComponent.Watching)
        {
            Player? player = level.Tracker.GetEntity<Player>();
            if (player != null)
            {
                effect = MiaoNetGraphics.RadialAlphaMaskEffect;
                // TODO scaling?
                effect.Parameters["Dimensions"].SetValue(new Vector2(320f, 180f));
                effect.Parameters["CenterPos"].SetValue(player.Center - level.Camera.Position);
                effect.Parameters["MinAlpha"].SetValue(settings.MinPlayerOpacityValue);
            }
        }

        // target gameplay, draw with a global alpha
        // with optionally distance based alpha
        gd.SetRenderTarget(GameplayBuffers.Gameplay);
        Draw.SpriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend, 
            SamplerState.PointClamp,
            DepthStencilState.None, 
            RasterizerState.CullNone, 
            effect,
            level.Camera.Matrix
        );
        Draw.SpriteBatch.Draw(GameplayBuffers.TempA, level.Camera.Position, Color.White * settings.PlayerOpacityValue);
        Draw.SpriteBatch.End();

        GameplayRenderer.Begin();
    }
}
