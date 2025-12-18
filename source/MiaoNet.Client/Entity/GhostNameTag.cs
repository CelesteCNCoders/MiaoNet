namespace Celeste.Mod.MiaoNet;

public sealed class GhostNameTag : Entity
{
    private readonly Entity entity;

    public string Text { get; set; }

    public GhostNameTag(Entity entity, string text)
    {
        Tag = TagsExt.SubHUD | entity.Tag;
        this.entity = entity;
        Text = text;
    }

    public GhostNameTag(MiaoNetGhost ghost)
        : this(ghost, ghost.Name)
    {
    }

    public override void Render()
    {
        base.Render();
        Vector2 worldPos = entity.Position;
        worldPos.Y -= 16f;
        MiaoNetFont.DrawGhostName(
            Text,
            SceneAs<Level>().WorldToScreen(worldPos),
            Color.White * (MiaoNetModule.Settings.NameOpacity / 10.0f)
        );
    }
}
