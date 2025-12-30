namespace Celeste.Mod.MiaoNet;

public sealed class Fireworks : Entity
{
    private readonly DeathEffect effect;
    private float speed;

    public Fireworks(Vector2 position, Color color, float initialSpeed)
        : base(position)
    {
        Add(effect = new DeathEffect(color)
        {
            OnEnd = new Action(RemoveSelf),
            Active = false
        });
        Depth = Depths.Player - 1;

        speed = initialSpeed;
    }

    public override void Added(Scene scene)
    {
        base.Added(scene);
        Audio.Play(SFX.char_mad_predeath);
    }

    public override void Update()
    {
        base.Update();
        if (!effect.Active)
        {
            Position -= new Vector2(0f, speed) * Engine.RawDeltaTime;
            speed -= 512f * Engine.RawDeltaTime;

            if (speed < -96f)
            {
                effect.Active = true;
                Audio.Play(SFX.char_mad_death);
            }
        }
    }
}
