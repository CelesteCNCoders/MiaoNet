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
        Tag |= Tags.Persistent | Tags.TransitionUpdate | Tags.FrozenUpdate | Tags.PauseUpdate | Tags.Global;
        IsOnSelf = true;
    }

    public GhostNameTag(MiaoNetGhost ghost, string name)
        : this((Entity)ghost, name)
    {
        IsOnSelf = false;
    }

    public override void Render()
    {
        base.Render();

        Vector2 worldPosition = Entity.Position;
        worldPosition.Y -= 16f;
        float alpha = IsOnSelf
            ? MiaoNetModule.Settings.SelfNameOpacityValue
            : MiaoNetModule.Settings.NameOpacityValue;
        const float Scale = 1f / 2f;
        const float Margin = 8f;

        Vector2 position = ScreenClamper.ClampIntoScreen(
            SceneAs<Level>().WorldToScreen(worldPosition),
            MiaoNetFont.Measure(Text) * Scale,
            new Vector2(1f / 2f, 1f),
            Margin
        );

        MiaoNetFont.DrawOutlineBottomCentered(
            Text,
            position,
            Vector2.One * Scale,
            Color.White * alpha
        );
    }
}
