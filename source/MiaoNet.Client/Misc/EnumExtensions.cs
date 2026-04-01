namespace Celeste.Mod.MiaoNet;

internal static class EnumExtensions
{
    extension(SyncMode syncMode)
    {
        public bool HasReceive => syncMode.HasFlag(SyncMode.Receive);

        public bool HasSend => syncMode.HasFlag(SyncMode.Send);
    }
}
