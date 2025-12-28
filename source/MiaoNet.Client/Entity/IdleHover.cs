using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.MiaoNet;

public sealed class IdleHover : Entity
{
    private readonly Entity parentEntity;
    private readonly MTexture hoverTexture;
    private float timer;

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
        timer += Engine.RawDeltaTime;
    }

    public override void Render()
    {
        base.Render();
        Level level = SceneAs<Level>();
        Vector2 pos = parentEntity.Position;
        // - name offset - popup offset
        pos.Y -= 16f + 6f;
        pos = level.WorldToScreen(pos);
        pos.Y += 12f * MathF.Sin(timer * 4f);
        hoverTexture.DrawJustified(
            pos,
            new Vector2(0.5f, 1f),
            Color.White, 1f
        );
    }
}
