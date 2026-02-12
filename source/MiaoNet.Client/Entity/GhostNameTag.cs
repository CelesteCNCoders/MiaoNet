using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class GhostNameTag : MiaoNetEntity
{
    public bool IsOnSelf { get; }

    public Entity Entity { get; set; }

    public string Text { get; }

    public Color Color { get; }

    private GhostNameTag(Entity entity, string text, Color color)
    {
        Tag = MiaoNetTag.Tag | TagsExt.SubHUD;
        Entity = entity;
        Text = text;
        Color = color;
    }

    private GhostNameTag(Entity entity, PlayerInfo playerInfo)
        : this(entity, playerInfo.DisplayName, playerInfo.Color)
    {

    }

    public GhostNameTag(Player player, PlayerInfo playerInfo)
        : this((Entity)player, playerInfo)
    {
        IsOnSelf = true;
    }

    public GhostNameTag(MiaoNetGhost ghost, PlayerInfo playerInfo)
        : this((Entity)ghost, playerInfo)
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
            : MiaoNetModule.Settings.PlayerNameOpacityValue;
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
            Color * alpha
        );
    }
}
