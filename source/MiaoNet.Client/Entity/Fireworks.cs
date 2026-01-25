namespace Celeste.Mod.MiaoNet;

public sealed class Fireworks : MiaoNetEntity
{
    private readonly DeathEffect effect;
    private float speed;

    public Fireworks(Vector2 position, Color color, float initialSpeed)
        : base(position)
    {
        Tag = MiaoNetTag.Tag;

        float alpha = MiaoNetModule.Settings.PlayerOpacityValue;
        Add(effect = new DeathEffect(color * alpha)
        {
            OnEnd = new Action(RemoveSelf),
            Active = false
        });
        Depth = Depths.Top;

        speed = initialSpeed;
    }

    public override void Added(Scene scene)
    {
        base.Added(scene);
        var ins = Audio.Play(MiaoNetSFX.PlayerPreDeath, Position);
        ins?.setVolume(MiaoNetModule.Settings.PlayerAudioVolumeValue);
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
                if (!Scene.Paused)
                {
                    var ins = Audio.Play(MiaoNetSFX.PlayerDeath, Position);
                    ins?.setVolume(MiaoNetModule.Settings.PlayerAudioVolumeValue);
                }
            }
        }
    }
}
