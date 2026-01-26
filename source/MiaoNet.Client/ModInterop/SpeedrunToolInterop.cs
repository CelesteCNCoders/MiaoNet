#pragma warning disable CA2211

using System.Diagnostics.CodeAnalysis;
using MonoMod.ModInterop;

namespace Celeste.Mod.MiaoNet;

[ModImportName("SpeedrunTool.SaveLoad")]
public static class SpeedrunToolInterop
{
    public static Action<Func<Type, bool>>? AddReturnSameObjectProcessor;
    public static Action<Func<Type, bool>>? RemoveReturnSameObjectProcessor;

    public delegate object RegisterSaveLoadActionHandler(
        Action<Dictionary<Type, Dictionary<string, object>>, Level>? saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level>? loadState,
        Action? clearState,
        Action<Level>? beforeSaveState,
        Action<Level>? beforeLoadState,
        Action? preCloneEntities
    );

    public static RegisterSaveLoadActionHandler? RegisterSaveLoadAction;
    public static Action<object>? Unregister;

    public static Action<Entity, bool>? IgnoreSaveState;
}

public static class SpeedrunToolCompat
{
    private static object? saveLoadAction;

    public static void Load()
    {
        typeof(SpeedrunToolInterop).ModInterop();
        if (SpeedrunToolInterop.IgnoreSaveState is null)
            return;

        SpeedrunToolInterop.AddReturnSameObjectProcessor!(CanReturnSameObject);
        saveLoadAction = SpeedrunToolInterop.RegisterSaveLoadAction!(
            null,
            (_, level) => { MiaoNetModule.OnLoadState(level); },
            null, null, null, null
        );
    }

    public static void Unload()
    {
        if (SpeedrunToolInterop.IgnoreSaveState is null)
            return;

        SpeedrunToolInterop.RemoveReturnSameObjectProcessor!(CanReturnSameObject);
        SpeedrunToolInterop.Unregister!(saveLoadAction!);
    }

    private static bool CanReturnSameObject(Type type)
        => type.Assembly == typeof(MiaoNetContext).Assembly;
}