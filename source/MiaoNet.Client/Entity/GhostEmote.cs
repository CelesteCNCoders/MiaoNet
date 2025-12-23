using System.Collections;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class GhostEmote : Entity
{
    public const float FixedSize = 128f;

    private float timer;
    private float popupAlpha = 1f;
    private float popupScale = 1f;
    private readonly Entity target;

    private readonly BakedEmoteData? emote;
    private readonly string? text;

    private GhostEmote(Entity target)
    {
        Tag |= Tags.FrozenUpdate | Tags.HUD | Tags.PauseUpdate | Tags.Persistent | Tags.TransitionUpdate;
        this.target = target;
        Add(new Coroutine(Routine()));
    }

    public GhostEmote(Entity target, BakedEmoteData emote)
        : this(target)
    {
        this.emote = emote;
    }

    public GhostEmote(Entity target, string text)
        : this(target)
    {
        this.text = text;
    }

    public override void Update()
    {
        base.Update();
        timer += Engine.RawDeltaTime;
    }

    private IEnumerator Routine()
    {
        float animTimer = 0.1f;

        while (animTimer >= 0f)
        {
            float t = 1f - animTimer / 0.1f;
            popupAlpha = Ease.CubeOut(t);
            popupScale = Ease.ElasticOut(t);

            animTimer -= Engine.RawDeltaTime;
            yield return null;
        }

        popupAlpha = 1f;
        popupScale = 1f;
        yield return 1f;

        animTimer = 0.5f;
        while (animTimer >= 0f)
        {
            float t = 1f - animTimer / 1f;
            popupAlpha = 1f - Ease.CubeIn(t);
            popupScale = 1f - 0.25f * Ease.CubeIn(t);

            animTimer -= Engine.RawDeltaTime;
            yield return null;
        }

        RemoveSelf();
        yield break;
    }

    public override void Render()
    {
        base.Render();
        Vector2 position = target.Position;
        // - name offset - popup offset
        position.Y -= 16f + 6f;
        position = SceneAs<Level>().WorldToScreen(position);
        if (emote is not null)
        {
            var texture = emote.Sample(timer);
            float scale = FixedSize / Math.Max(texture.Width, texture.Height);
            texture.DrawJustified(position, new Vector2(0.5f, 1f), Color.White * popupAlpha, popupScale * scale);
        }
        else
        {
            SafeGuard.Assert(text is not null);
            Vector2 size = MiaoNetFont.Measure(text);
            float scale = Math.Min(1f, (FixedSize * 4f) / size.X);
            MiaoNetFont.DrawOutlineBottomCentered(text, position, Vector2.One * popupScale * scale, Color.White * popupAlpha);
        }
    }
}
