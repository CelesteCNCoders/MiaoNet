#pragma warning disable CA2211

using MonoMod.ModInterop;

namespace Celeste.Mod.MiaoNet;

[ModImportName("SpeedrunTool.SaveLoad")]
public static class SpeedrunToolInterop
{
    public static Action<Func<Type, bool>>? AddReturnSameObjectProcessor;
    public static Action<Func<Type, bool>>? RemoveReturnSameObjectProcessor;

    public static Action<Func<object, object?>>? AddCustomDeepCloneProcessor;
    public static Action<Func<object, object?>>? RemoveCustomDeepCloneProcessor;
}

public static class SpeedrunToolCompat
{
    public static void Load()
    {
        typeof(SpeedrunToolInterop).ModInterop();
        if (SpeedrunToolInterop.AddReturnSameObjectProcessor is null)
            return;

        SpeedrunToolInterop.AddReturnSameObjectProcessor!(CanReturnSameObject);
        SpeedrunToolInterop.AddCustomDeepCloneProcessor!(CustomDeepClone);
    }

    public static void Unload()
    {
        if (SpeedrunToolInterop.AddReturnSameObjectProcessor is null)
            return;

        SpeedrunToolInterop.RemoveReturnSameObjectProcessor!(CanReturnSameObject);
        SpeedrunToolInterop.RemoveCustomDeepCloneProcessor!(CustomDeepClone);
    }

    private static bool CanReturnSameObject(Type type)
        => type.Assembly == typeof(MiaoNetContext).Assembly && !type.IsSubclassOf(typeof(Entity));

    private static object? CustomDeepClone(object obj)
    {
        Type type = obj.GetType();
        if (type.Assembly == typeof(MiaoNetContext).Assembly && type.IsSubclassOf(typeof(Entity)))
        {
            var curScene = Engine.Scene;
            var entity = (Entity)obj;
            entity.RemoveSelf();
            curScene.Add(entity);
            return obj;
        }
        return null;
    }
}