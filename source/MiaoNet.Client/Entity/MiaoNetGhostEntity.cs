using System.Runtime.CompilerServices;
using FMOD.Studio;

namespace Celeste.Mod.MiaoNet;

[Tracked(inherited: true)]
public abstract class MiaoNetGhostEntity : MiaoNetEntity
{
    protected MiaoNetGhostEntity()
    {
    }

    protected MiaoNetGhostEntity(Vector2 position) : base(position)
    {
    }

    public void OnPlayAudio(string @event)
        => OnPlayAudio(@event, null, 0f);

    public void OnPlayAudio(string @event, string? param, float paramValue)
    {
        var settings = MiaoNetModule.Settings;
        if (!settings.PlayerAudio || Scene is not Level level || level.Paused)
            return;

        EventDescription eventDescription = Audio.GetEventDescription(@event);
        if (eventDescription is null)
            return;

        eventDescription.is3D(out var is3D);

        // TODO prevent this earlier server-side
        if (!level.InsideCamera(Center, is3D ? 128f : 64f))
            return;

        eventDescription.createInstance(out var instance);

        if (instance is null)
            return;

        if (is3D)
            Audio.Position(instance, Center);

        float volume = MiaoNetModule.Settings.PlayerAudioVolumeValue;
        instance.setVolume(volume);

        if (param is not null)
            instance.setParameterValue(param, paramValue);

        instance.start();
        instance.release();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void BaseRender() => base.Render();

    public sealed override void Render() 
    {
        // do nothing as if it's invisible
        // but do not set Visible to false
        // or its component will skip rendering
        // see GhostRenderLayerEntity
    }

    public abstract void GhostRender();
}
