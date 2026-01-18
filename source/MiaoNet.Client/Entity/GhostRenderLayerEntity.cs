namespace Celeste.Mod.MiaoNet;

public sealed class GhostRenderLayerEntity : Entity
{
    private readonly bool isHigh;

    public GhostRenderLayerEntity(bool isHigh)
    {
        Tag |= Tags.Global | Tags.PauseUpdate | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.Persistent;
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
        {
            foreach (GhostFollower follower in level.Tracker.GetEntities<GhostFollower>().Cast<GhostFollower>())
            {
                if (isHigh ? follower.Depth <= Depth : follower.Depth >= Depth)
                    follower.GhostRender();
            }
            foreach (MiaoNetGhost ghost in level.Tracker.GetEntities<MiaoNetGhost>().Cast<MiaoNetGhost>())
            {
                if (isHigh ? ghost.Depth <= Depth : ghost.Depth >= Depth)
                    ghost.GhostRender();
            }
        }
        GameplayRenderer.End();

        gd.SetRenderTarget(GameplayBuffers.Gameplay);

        GameplayRenderer.Begin();

        float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
        Draw.SpriteBatch.Draw(GameplayBuffers.TempA, level.Camera.Position, Color.White * alpha);
    }
}
