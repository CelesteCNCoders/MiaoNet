using System.Runtime.CompilerServices;
using FMOD.Studio;

namespace Celeste.Mod.MiaoNet;

public enum GhostRenderBand
{
    Normal,
    DreamDash,
    High,
}

[Tracked(inherited: true)]
public abstract class MiaoNetGhostEntity : MiaoNetEntity
{
    public virtual GhostRenderBand RenderBand
        => Depth <= Depths.Top ? GhostRenderBand.High : GhostRenderBand.Normal;

    public virtual bool WatchPresentationFocus => false;

    protected float EffectiveOpacity => WatchPresentationFocus
        ? 1f
        : MiaoNetModule.Settings.PlayerOpacityValue;

    protected MiaoNetGhostEntity()
    {
        Add(new MirrorReflection());
    }

    protected MiaoNetGhostEntity(Vector2 position) : base(position)
    {
        Add(new MirrorReflection());
    }

    public void OnPlayAudio(string @event)
        => OnPlayAudio(@event, null, 0f);

    public void OnPlayAudio(string @event, string? param, float paramValue)
    {
        var settings = MiaoNetModule.Settings;
        if (!settings.PlayerAudioSyncMode.HasReceive || Scene is not Level level || level.Paused)
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
        // Ordinary ghost rendering is composited by GhostRenderLayerEntity. The
        // vanilla mirror renderer deliberately calls Entity.Render while its
        // MirrorReflection is active, so allow that one render path through.
        if (MiaoNetModule.IsWatching
            && Get<MirrorReflection>() is { IsRendering: true })
            GhostRender();
    }

    public abstract void GhostRender();
}
