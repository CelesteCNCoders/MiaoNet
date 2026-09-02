namespace Celeste.Mod.MiaoNet;

internal static class WatchSceneAudioSuppression
{
    // Remote scene state is applied synchronously on the game thread; depth keeps nested applications scoped.
    [ThreadStatic]
    private static int suppressionDepth;

    public static void Load()
    {
        On.Celeste.Audio.Play_string += Audio_Play;
        On.Celeste.Audio.Play_string_string_float += Audio_Play;
        On.Celeste.Audio.Play_string_Vector2 += Audio_Play;
        On.Celeste.Audio.Play_string_Vector2_string_float += Audio_Play;
        On.Celeste.Audio.Play_string_Vector2_string_float_string_float += Audio_Play;
        On.Celeste.SoundSource.Play += SoundSource_Play;
        On.Celeste.SoundEmitter.Play_string += SoundEmitter_Play;
    }

    public static void Unload()
    {
        On.Celeste.SoundSource.Play -= SoundSource_Play;
        On.Celeste.SoundEmitter.Play_string -= SoundEmitter_Play;
        On.Celeste.Audio.Play_string_Vector2_string_float_string_float -= Audio_Play;
        On.Celeste.Audio.Play_string_Vector2_string_float -= Audio_Play;
        On.Celeste.Audio.Play_string_Vector2 -= Audio_Play;
        On.Celeste.Audio.Play_string_string_float -= Audio_Play;
        On.Celeste.Audio.Play_string -= Audio_Play;
        suppressionDepth = 0;
    }

    public static IDisposable Begin()
    {
        suppressionDepth++;
        return new Scope();
    }

    private static FMOD.Studio.EventInstance Audio_Play(
        On.Celeste.Audio.orig_Play_string orig,
        string path
    ) => suppressionDepth > 0 ? null! : orig(path);

    private static FMOD.Studio.EventInstance Audio_Play(
        On.Celeste.Audio.orig_Play_string_string_float orig,
        string path,
        string param,
        float value
    ) => suppressionDepth > 0 ? null! : orig(path, param, value);

    private static FMOD.Studio.EventInstance Audio_Play(
        On.Celeste.Audio.orig_Play_string_Vector2 orig,
        string path,
        Vector2 position
    ) => suppressionDepth > 0 ? null! : orig(path, position);

    private static FMOD.Studio.EventInstance Audio_Play(
        On.Celeste.Audio.orig_Play_string_Vector2_string_float orig,
        string path,
        Vector2 position,
        string param,
        float value
    ) => suppressionDepth > 0 ? null! : orig(path, position, param, value);

    private static FMOD.Studio.EventInstance Audio_Play(
        On.Celeste.Audio.orig_Play_string_Vector2_string_float_string_float orig,
        string path,
        Vector2 position,
        string param,
        float value,
        string param2,
        float value2
    ) => suppressionDepth > 0 ? null! : orig(path, position, param, value, param2, value2);

    private static SoundSource SoundSource_Play(
        On.Celeste.SoundSource.orig_Play orig,
        SoundSource self,
        string path,
        string? param,
        float value
    )
    {
        if (suppressionDepth > 0)
            return self;
        return orig(self, path, param, value);
    }

    private static SoundEmitter SoundEmitter_Play(On.Celeste.SoundEmitter.orig_Play_string orig, string path)
    {
        if (suppressionDepth > 0)
            return null!;
        return orig(path);
    }

    private sealed class Scope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            suppressionDepth--;
        }
    }
}
