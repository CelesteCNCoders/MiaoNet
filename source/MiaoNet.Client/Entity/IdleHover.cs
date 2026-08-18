using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.MiaoNet;

public sealed class IdleHover : MiaoNetEntity
{
    private readonly Entity parentEntity;
    private readonly MTexture hoverTexture;
    private float timer;
    private float scale = 1f;

    private const float OutAnimDuration = 1f / 3f;
    private const float InAnimDuration = 1f / 5f;
    private Tween? animTween;

    public IdleHover(Entity parentEntity)
    {
        Tag |= parentEntity.Tag | TagsExt.SubHUD;
        hoverTexture = GFX.Gui["hover/idle"];
        Depth = Depths.FakeWalls - 1;
        this.parentEntity = parentEntity;
    }

    public override void Update()
    {
        base.Update();
        if (animTween is null)
            timer += Engine.RawDeltaTime;
    }

    public void PlayAnimation()
    {
        animTween = Tween.Set(
            this, Tween.TweenMode.Oneshot,
            OutAnimDuration, Ease.ElasticOut,
            t => scale = MathHelper.Lerp(0.5f, 1f, t.Eased),
            t => animTween = null
        );
        animTween.UseRawDeltaTime = true;
    }

    public void StopAnimationAndRemove()
    {
        animTween = Tween.Set(
            this, Tween.TweenMode.Oneshot,
            InAnimDuration, Ease.ElasticIn,
            t => scale = MathHelper.Lerp(1f, 0.5f, t.Eased),
            t => { animTween = null; RemoveSelf(); }
        );
        animTween.UseRawDeltaTime = true;
    }

    public override void Render()
    {
        base.Render();

        var settings = MiaoNetModule.Settings;

        if (settings.GroupPhotoMode)
            return;
        
        Level level = SceneAs<Level>();
        Vector2 pos = parentEntity.Position;
        // - name offset - popup offset
        pos.Y -= 16f + 6f;
        pos = level.WorldToScreen(pos);
        pos.Y += 12f * MathF.Sin(timer * 4f);
        hoverTexture.DrawJustified(
            pos,
            new Vector2(0.5f, 1f),
            Color.White * settings.PlayerOpacityValue, 
            scale
        );
    }
}
