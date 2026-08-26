using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed class GhostRenderLayerEntity : MiaoNetEntity
{
    private readonly GhostRenderBand band;

    public GhostRenderLayerEntity(GhostRenderBand band)
    {
        Tag = MiaoNetTag.Normal;
        Depth = band switch
        {
            GhostRenderBand.Normal => Depths.Player + 1,
            GhostRenderBand.DreamDash => Depths.PlayerDreamDashing,
            GhostRenderBand.High => Depths.Top,
            _ => throw new ArgumentOutOfRangeException(nameof(band)),
        };
        this.band = band;
    }

    public override void Render()
    {
        Level level = SceneAs<Level>();

        if (level.Tracker.GetEntities<MiaoNetGhostEntity>().Count == 0)
            return;

        var gd = Engine.Instance.GraphicsDevice;
        var settings = MiaoNetModule.Settings;

        GameplayRenderer.End();

        List<MiaoNetGhostEntity> entities = level.Tracker.GetEntities<MiaoNetGhostEntity>()
            .Cast<MiaoNetGhostEntity>()
            .Where(entity => entity.RenderBand == band && entity.Visible)
            .ToList();

        DrawGhostPass(gd, entities.Where(static entity => !entity.WatchPresentationFocus));

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

        // The explicitly watched subject is a viewing target, not an ordinary
        // background multiplayer ghost. Composite it at full opacity without
        // changing the user's opacity setting for everyone else.
        if (entities.Any(static entity => entity.WatchPresentationFocus))
        {
            DrawGhostPass(gd, entities.Where(static entity => entity.WatchPresentationFocus));
            gd.SetRenderTarget(GameplayBuffers.Gameplay);
            Draw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                level.Camera.Matrix
            );
            Draw.SpriteBatch.Draw(GameplayBuffers.TempA, level.Camera.Position, Color.White);
            Draw.SpriteBatch.End();
        }

        GameplayRenderer.Begin();
    }

    private static void DrawGhostPass(
        GraphicsDevice graphicsDevice,
        IEnumerable<MiaoNetGhostEntity> entities
    )
    {
        graphicsDevice.SetRenderTarget(GameplayBuffers.TempA);
        graphicsDevice.Clear(Color.Transparent);
        GameplayRenderer.Begin();
        foreach (MiaoNetGhostEntity entity in entities)
            entity.GhostRender();
        GameplayRenderer.End();
    }
}
