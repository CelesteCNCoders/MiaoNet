using System.Collections;

namespace Celeste.Mod.MiaoNet;

public sealed class MiaoNetGhostEmote : Entity
{
    private float timer;
    private float alpha = 1f;
    private float scale = 1f;
    private readonly Entity target;
    private readonly BakedEmoteData emote;

    public MiaoNetGhostEmote(Entity target, BakedEmoteData emote)
    {
        Tag |= Tags.FrozenUpdate | Tags.HUD | Tags.PauseUpdate | Tags.Persistent | Tags.TransitionUpdate;
        this.target = target;
        this.emote = emote;
        Add(new Coroutine(Routine()));
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
            alpha = Ease.CubeOut(t);
            scale = Ease.ElasticOut(t);

            animTimer -= Engine.RawDeltaTime;
            yield return null;
        }

        alpha = 1f;
        scale = 1f;
        yield return 1f;

        animTimer = 0.5f;
        while (animTimer >= 0f)
        {
            float t = 1f - animTimer / 1f;
            alpha = 1f - Ease.CubeIn(t);
            scale = 1f - 0.25f * Ease.CubeIn(t);

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
        var texture = emote.Sample(timer);
        texture.DrawJustified(position, new Vector2(0.5f, 1f), Color.White * alpha, scale);
    }
}
