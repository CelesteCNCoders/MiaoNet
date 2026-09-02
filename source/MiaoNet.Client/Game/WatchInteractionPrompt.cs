using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Replays the visual part of a remote TalkComponent without exposing an
/// OnTalk callback or consulting the Watcher's hidden local Player.
/// </summary>
internal sealed class WatchInteractionPrompt : MiaoNetEntity
{
    private readonly Entity owner;
    private readonly MTexture idleTexture = GFX.Gui["hover/idle"];
    private Vector2 drawAt;
    private bool displayed;
    private float slide;
    private float timer;

    internal WatchInteractionPrompt(Entity owner)
    {
        this.owner = owner;
        Tag |= owner.Tag | TagsExt.SubHUD;
        Depth = Depths.FakeWalls - 1;
    }

    internal void SetPresentation(bool display, Vector2 remoteDrawAt)
    {
        displayed = display;
        drawAt = remoteDrawAt;
    }

    public override void Update()
    {
        if (!MiaoNetModule.IsWatching || owner.Scene is null || !ReferenceEquals(owner.Scene, Scene))
        {
            RemoveSelf();
            return;
        }

        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;

        timer += Engine.DeltaTime;
        slide = Calc.Approach(slide, displayed ? 1f : 0f, Engine.DeltaTime * 4f);
        if (!displayed && slide <= 0f)
        {
            RemoveSelf();
            return;
        }

        base.Update();
    }

    public override void Render()
    {
        if (slide <= 0f || !owner.Visible || MiaoNetModule.Settings.GroupPhotoMode)
            return;

        Level level = SceneAs<Level>();
        Vector2 position = level.WorldToScreen(owner.Position + drawAt);
        position.Y += 12f * MathF.Sin(timer * 4f)
            + 64f * (1f - Ease.CubeOut(slide));
        idleTexture.DrawJustified(
            position,
            new Vector2(0.5f, 1f),
            Color.White * Ease.CubeInOut(slide),
            1f
        );
    }
}

internal static class WatchInteractionPromptPresentation
{
    private static readonly ConditionalWeakTable<Entity, WatchInteractionPrompt> prompts = new();

    internal static void Apply(Level level, Entity owner, bool displayed, Vector2 drawAt)
    {
        if (!displayed && !prompts.TryGetValue(owner, out _))
            return;

        WatchInteractionPrompt prompt = prompts.GetValue(
            owner,
            static entity => new WatchInteractionPrompt(entity)
        );
        prompt.SetPresentation(displayed, drawAt);
        if (displayed && prompt.Scene is null)
            level.Add(prompt);
    }

    internal static void Hide(Entity owner)
    {
        if (prompts.TryGetValue(owner, out WatchInteractionPrompt? prompt))
            prompt.SetPresentation(false, Vector2.Zero);
    }
}
