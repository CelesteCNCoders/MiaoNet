namespace Celeste.Mod.MiaoNet;

public sealed class GhostRenderLayerEntity : MiaoNetEntity
{
    private readonly bool isHigh;

    public GhostRenderLayerEntity(bool isHigh)
    {
        Tag = MiaoNetTag.Tag;
        Depth = isHigh ? Depths.Top : Depths.Player;
        this.isHigh = isHigh;
    }

    public override void Render()
    {
        var gd = Engine.Instance.GraphicsDevice;
        Level level = SceneAs<Level>();

        GameplayRenderer.End();

        gd.SetRenderTarget(GameplayBuffers.TempA);
        gd.Clear(Color.Transparent);

        GameplayRenderer.Begin();

        foreach (MiaoNetGhostEntity entity in level.Tracker.GetEntities<MiaoNetGhostEntity>().Cast<MiaoNetGhostEntity>())
        {
            if (isHigh ? entity.Depth <= Depth : entity.Depth >= Depth && entity.Visible)
                entity.GhostRender();
        }

        GameplayRenderer.End();

        gd.SetRenderTarget(GameplayBuffers.Gameplay);

        GameplayRenderer.Begin();

        float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
        Draw.SpriteBatch.Draw(GameplayBuffers.TempA, level.Camera.Position, Color.White * alpha);
    }
}
