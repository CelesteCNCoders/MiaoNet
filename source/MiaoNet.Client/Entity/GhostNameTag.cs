namespace Celeste.Mod.MiaoNet;

public sealed class GhostNameTag : Entity
{
    public Entity Entity { get; set; }

    public string Text { get; set; }

    public GhostNameTag(Entity entity, string text)
    {
        Tag = TagsExt.SubHUD | entity.Tag;
        Entity = entity;
        Text = text;
    }

    public GhostNameTag(MiaoNetGhost ghost)
        : this(ghost, ghost.Name)
    {
    }

    public override void Render()
    {
        base.Render();
        Vector2 worldPos = Entity.Position;
        worldPos.Y -= 16f;
        MiaoNetFont.DrawOutlineBottomCentered(
            Text,
            SceneAs<Level>().WorldToScreen(worldPos),
            Vector2.One / 2f,
            Color.White * (MiaoNetModule.Settings.NameOpacity / 10.0f)
        );
    }
}
