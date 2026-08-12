using System.Collections;

namespace Celeste.Mod.MiaoNet;

public sealed class GhostDeadBody : MiaoNetGhostEntity
{
    private readonly PlayerHair hair;
    private readonly PlayerSprite sprite;
    private readonly VertexLight? vertexLight;

    private Color initialHairColor;
    private Vector2 bounce = Vector2.Zero;
    private DeathEffect? deathEffect;
    private Facings facing;
    private float scale = 1f;

    public GhostDeadBody(Vector2 position, Facings facing, PlayerHair hair, PlayerSprite sprite, VertexLight? vertexLight, Vector2 direction)
    {
        Tag = MiaoNetTag.Normal;

        Depth = Depths.Top;
        this.facing = facing;
        Position = position;
        Add(this.hair = hair);
        Add(this.sprite = sprite);
        sprite.Active = true;
        if (vertexLight is not null)
            Add(this.vertexLight = vertexLight);
        sprite.Color = Color.White;
        initialHairColor = hair.Color;
        bounce = direction;
        Add(new Coroutine(DeathRoutine()));
    }

    public override void Awake(Scene scene)
    {
        base.Awake(scene);
        if (bounce == Vector2.Zero)
            return;

        if (Math.Abs(bounce.X) > Math.Abs(bounce.Y))
        {
            sprite.Play("deadside");
            facing = (Facings)(-Math.Sign(bounce.X));
        }
        else
        {
            bounce = Calc.AngleToVector(
                Calc.AngleApproach(
                    bounce.Angle(),
                    new Vector2(-(float)facing, 0f).Angle(),
                    0.5f
                ),
                1f
            );
            sprite.Play(bounce.Y < 0f ? "deadup" : "deaddown");
        }
    }

    private IEnumerator DeathRoutine()
    {
        Level level = SceneAs<Level>();
        if (bounce != Vector2.Zero)
        {
            OnPlayAudio(MiaoNetSFX.PlayerPreDeath);
            yield return 0.05f; // freeze frames
            const float StartScale = 1.5f;
            scale = StartScale;
            yield return null;
            Vector2 from = Position;
            Vector2 to = from + bounce * 24f;
            Tween tween = Tween.Create(Tween.TweenMode.Oneshot, Ease.CubeOut, 0.5f, true);
            // TODO apply delta time
            tween.UseRawDeltaTime = true;
            Add(tween);
            tween.OnUpdate = t =>
            {
                Position = from + (to - from) * t.Eased;
                scale = StartScale - t.Eased * (StartScale - 1f);
                sprite.Rotation = (float)(Math.Floor(t.Eased * 4f) * MathF.Tau);
            };
            yield return tween.Duration * 0.75f;
            tween.Stop();
        }
        Position += Vector2.UnitY * -5f;
        if (!level.Paused)
        {
            float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
            level.Displacement.AddBurst(Position, 0.3f, 0f, 80f, alpha: alpha);
        }
        OnPlayAudio(MiaoNetSFX.PlayerDeath);
        deathEffect = new DeathEffect(initialHairColor, Center - Position);
        if (vertexLight is not null)
            deathEffect.OnUpdate = f => vertexLight.Alpha = 1f - f;
        Add(deathEffect);
        yield return deathEffect.Duration;
        sprite.Active = false;
        RemoveSelf();
    }

    public override void Update()
    {
        base.Update();
        hair.Color = sprite.CurrentAnimationFrame == 0 ? Color.White : initialHairColor;
    }

    public override void GhostRender()
    {
        if (deathEffect == null)
        {
            sprite.Scale.X = (float)facing * scale;
            sprite.Scale.Y = scale;
            hair.Facing = facing;
            BaseRender();
        }
        else
        {
            deathEffect.Render();
        }
    }
}
