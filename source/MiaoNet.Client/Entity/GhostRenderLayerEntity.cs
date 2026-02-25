using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.MiaoNet;

public sealed class GhostRenderLayerEntity : MiaoNetEntity
{
    private readonly bool isHigh;
    private readonly List<MiaoNetGhostEntity> transparentBatch = new();
    private readonly List<MiaoNetGhostEntity> opaqueBatch = new();

    private static Effect? followerRadialShader;

    public static void LoadContent()
    {
        if (Everest.Content.TryGet("Effects/RadialAlphaMask.cso", out ModAsset asset))
            followerRadialShader = new Effect(Engine.Graphics.GraphicsDevice, asset.Data);
    }

    public GhostRenderLayerEntity(bool isHigh)
    {
        Tag = MiaoNetTag.Tag;
        Depth = isHigh ? Depths.Top : (Depths.Player + 1);
        this.isHigh = isHigh;
    }

    public override void Render()
    {
        var gd = Engine.Instance.GraphicsDevice;
        Level level = SceneAs<Level>();
        var settings = MiaoNetModule.Settings;

        GameplayRenderer.End();

        transparentBatch.Clear();
        opaqueBatch.Clear();

        foreach (MiaoNetGhostEntity entity in level.Tracker.GetEntities<MiaoNetGhostEntity>().Cast<MiaoNetGhostEntity>())
        {
            if (isHigh ? entity.Depth <= Depth : entity.Depth >= Depth)
            {
                if (ShouldRenderTransparent(entity))
                    transparentBatch.Add(entity);
                else
                    opaqueBatch.Add(entity);
            }
        }

        gd.SetRenderTarget(GameplayBuffers.TempA);
        gd.Clear(Color.Transparent);
        GameplayRenderer.Begin();
        foreach (MiaoNetGhostEntity entity in transparentBatch)
            entity.GhostRender();
        GameplayRenderer.End();

        gd.SetRenderTarget(GameplayBuffers.TempB);
        gd.Clear(Color.Transparent);

        Effect? shader = followerRadialShader;
        bool useShader = shader != null && settings.PlayerFollowersVisibility == RemotePlayerVisibility.DistanceBased;
        Effect? activeEffect = null;
        float batchAlpha = 1f;

        if (useShader)
        {
            Player? player = level.Tracker.GetEntity<Player>();
            if (player != null)
            {
                shader!.Parameters["Time"].SetValue(level.TimeActive);
                shader.Parameters["CamPos"].SetValue(level.Camera.Position);
                shader.Parameters["Dimensions"].SetValue(new Vector2(320f, 180f));
                shader.Parameters["CenterPos"].SetValue(player.Center);
                shader.Parameters["FadeRadiusInner"].SetValue(settings.PlayerFollowersDistanceRadius);
                shader.Parameters["FadeRadiusOuter"].SetValue(settings.PlayerFollowersDistanceRadius + settings.PlayerFollowersDistanceFadeRadius);
                shader.Parameters["MinAlpha"].SetValue(0f);
                activeEffect = shader;
            }
        }

        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, activeEffect, level.Camera.Matrix);
        Draw.SpriteBatch.Draw(GameplayBuffers.TempA, level.Camera.Position, Color.White * batchAlpha);
        Draw.SpriteBatch.End();

        GameplayRenderer.Begin();
        foreach (MiaoNetGhostEntity entity in opaqueBatch)
            entity.GhostRender();
        GameplayRenderer.End();

        gd.SetRenderTarget(GameplayBuffers.Gameplay);
        GameplayRenderer.Begin();
        float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
        Draw.SpriteBatch.Draw(GameplayBuffers.TempB, level.Camera.Position, Color.White * alpha);
    }

    private static bool ShouldRenderTransparent(MiaoNetGhostEntity entity)
    {
        if (entity is not GhostFollower)
            return false;

        var settings = MiaoNetModule.Settings;
        return settings.PlayerFollowersVisibility == RemotePlayerVisibility.DistanceBased;
    }
}
