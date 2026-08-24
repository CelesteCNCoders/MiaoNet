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
        Tag = MiaoNetTag.Hud;
        Entity = entity;
        Text = text;
        Color = color;
    }

    private GhostNameTag(Entity entity, OnlinePlayer onlinePlayer, bool avatar)
        : this(entity, onlinePlayer.GetDisplayName(true, avatar), onlinePlayer.Info.Color)
    {

    }

    public GhostNameTag(Player player, OnlinePlayer onlinePlayer, bool avatar)
        : this((Entity)player, onlinePlayer, avatar)
    {
        IsOnSelf = true;
    }

    public GhostNameTag(MiaoNetGhost ghost, OnlinePlayer onlinePlayer, bool avatar)
        : this((Entity)ghost, onlinePlayer, avatar)
    {
        IsOnSelf = false;
    }

    public override void Render()
    {
        base.Render();

        const float BaseScale = 1f / 2f;
        const float Margin = 8f;
        const float FadeRadius = 128f;

        float scale = BaseScale * SceneAs<Level>().Zoom;

        var f = ExtendedVariantInterop.GetCurrentVariantValue;
        bool upsideDown = f is not null && (bool)f.Invoke("UpsideDown");

        Vector2 justify = new Vector2(1f / 2f, !upsideDown ? 1f : 0f);

        Vector2 worldPosition = Entity.Position;
        worldPosition.Y -= 16f;

        Vector2 position = SceneAs<Level>().WorldToScreen(worldPosition);
        if (upsideDown)
            position.Y = Celeste.TargetHeight - position.Y;
        Vector2 clampedPosition = ScreenClamper.ClampIntoScreen(
            position,
            MiaoNetFont.Measure(Text) * scale,
            justify,
            Margin
        );

        var settings = MiaoNetModule.Settings;
        float alpha = IsOnSelf
            ? settings.SelfNameOpacityValue
            : position == clampedPosition
                ? settings.PlayerNameOpacityValue
                : Calc.LerpClamp(
                    settings.PlayerNameOpacityValue,
                    settings.OffScreenPlayerNameOpacityValue,
                    Vector2.DistanceSquared(position, clampedPosition) / (FadeRadius * FadeRadius)
                );
        MiaoNetFont.DrawOutline(
            Text,
            clampedPosition,
            justify,
            Vector2.One * scale,
            Color * alpha
        );
    }
}
