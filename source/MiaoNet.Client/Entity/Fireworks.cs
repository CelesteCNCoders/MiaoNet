namespace Celeste.Mod.MiaoNet;

public sealed class Fireworks : MiaoNetGhostEntity
{
    public Fireworks(Vector2 position, Color color, float initialSpeed)
        : base(position)
    {
        Tag = MiaoNetTag.Tag;

        Add(new FireworksComponent(color, initialSpeed));
        Depth = Depths.Top;
    }

    public override void GhostRender()
        => BaseRender();
}

// inherit from MiaoNetEntity so it won't be effected by render layer entity
public sealed class SelfFireworks : MiaoNetEntity
{
    public SelfFireworks(Vector2 position, Color color, float initialSpeed)
        : base(position)
    {
        Tag = MiaoNetTag.Tag;

        Add(new FireworksComponent(color, initialSpeed));
        Depth = Depths.Top;
    }
}