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

        List<Entity> entities = level.Tracker.GetEntities<MiaoNetGhostEntity>();
        if (entities.Count == 0)
            return;

        var gd = Engine.Instance.GraphicsDevice;
        var settings = MiaoNetModule.Settings;

        GameplayRenderer.End();

        bool hasFocusedEntity = DrawGhostPass(gd, entities, drawFocused: false);

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
        if (hasFocusedEntity)
        {
            DrawGhostPass(gd, entities, drawFocused: true);
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

    private bool DrawGhostPass(
        GraphicsDevice graphicsDevice,
        List<Entity> entities,
        bool drawFocused
    )
    {
        bool hasFocusedEntity = false;
        graphicsDevice.SetRenderTarget(GameplayBuffers.TempA);
        graphicsDevice.Clear(Color.Transparent);
        GameplayRenderer.Begin();
        foreach (Entity trackedEntity in entities)
        {
            MiaoNetGhostEntity entity = (MiaoNetGhostEntity)trackedEntity;
            if (entity.RenderBand != band || !entity.Visible)
                continue;
            if (entity.WatchPresentationFocus)
                hasFocusedEntity = true;
            if (entity.WatchPresentationFocus != drawFocused)
                continue;
            entity.GhostRender();
        }
        GameplayRenderer.End();
        return hasFocusedEntity;
    }
}
