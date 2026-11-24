namespace Celeste.Mod.MiaoNet;

partial class MiaoNetCommand
{
    private static string? ShareClear(Context context)
    {
        LevelClearSharing.TryShareLastClear(context.MiaoNetContext, out string? error);
        return error;
    }
}
