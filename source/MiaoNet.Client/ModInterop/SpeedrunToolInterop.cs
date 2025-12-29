#pragma warning disable CA2211

using MonoMod.ModInterop;

namespace Celeste.Mod.MiaoNet;

[ModImportName("SpeedrunTool.SaveLoad")]
public static class SpeedrunToolInterop
{
    public static Action<Func<Type, bool>>? AddReturnSameObjectProcessor;
}
