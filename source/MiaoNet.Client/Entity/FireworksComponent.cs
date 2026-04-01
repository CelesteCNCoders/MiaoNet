namespace Celeste.Mod.MiaoNet;

public sealed class FireworksComponent : Component
{
    private readonly Color color;
    private float speed;
    private float percent;
    private bool blooming;

    public FireworksComponent(Color color, float initialSpeed)
        : base(true, true)
    {
        this.color = color;
        speed = initialSpeed;
    }

    public override void EntityAdded(Scene scene)
    {
        base.EntityAdded(scene);
        var settings = MiaoNetModule.Settings;
        if (!settings.PlayerAudioSyncMode.HasReceive || scene.Paused)
            return;
        var ins = Audio.Play(MiaoNetSFX.PlayerPreDeath, Entity.Position);
        ins?.setVolume(settings.PlayerAudioVolumeValue);
    }

    public override void Update()
    {
        base.Update();
        if (!blooming)
        {
            Entity.Position -= new Vector2(0f, speed) * Engine.RawDeltaTime;
            speed -= 512f * Engine.RawDeltaTime;
            if (speed <= -96f)
            {
                blooming = true;

                var settings = MiaoNetModule.Settings;
                if (!settings.PlayerAudioSyncMode.HasReceive || Scene.Paused)
                    return;

                var ins = Audio.Play(MiaoNetSFX.PlayerDeath, Entity.Position);
                ins?.setVolume(settings.PlayerAudioVolumeValue);
            }
        }
        else if (percent < 1f)
        {
            const float Duration = 0.873f;
            percent = Calc.Approach(percent, 1f, Engine.DeltaTime / Duration);
            if (percent == 1f)
                Entity.RemoveSelf();
        }
    }

    public override void Render()
    {
        base.Render();
        DeathEffect.Draw(Entity.Position, color, percent);
    }
}
