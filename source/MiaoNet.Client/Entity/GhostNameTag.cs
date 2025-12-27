namespace Celeste.Mod.MiaoNet;

public sealed class GhostNameTag : Entity
{
    public bool IsOnSelf { get; }

    public Entity Entity { get; set; }

    public string Text { get; }

    private GhostNameTag(Entity entity, string text)
    {
        Tag = TagsExt.SubHUD | entity.Tag;
        Entity = entity;
        Text = text;
    }

    public GhostNameTag(Player player, string name)
        : this((Entity)player, name)
    {
        IsOnSelf = true;
    }

    public GhostNameTag(MiaoNetGhost ghost)
        : this(ghost, ghost.Name)
    {
        IsOnSelf = false;
    }

    public override void Render()
    {
        base.Render();
        Vector2 worldPos = Entity.Position;
        worldPos.Y -= 16f;
        float alpha = IsOnSelf 
            ? MiaoNetModule.Settings.SelfNameOpacityValue 
            : MiaoNetModule.Settings.NameOpacityValue;
        MiaoNetFont.DrawOutlineBottomCentered(
            Text,
            SceneAs<Level>().WorldToScreen(worldPos),
            Vector2.One / 2f,
            Color.White * alpha
        );
    }
}
