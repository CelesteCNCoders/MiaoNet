// 始终将最近一次通关分享到全局频道，与当前聊天标签无关。
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

partial class MiaoNetCommand
{
    private static long? lastClearShare;

    private static string? ShareClear(Context context)
    {
        if (!context.MiaoNetContext.HasConnection || context.MiaoNetContext.ClientState is null)
            return Dialog.Clean("miaonet_clear_not_connected");
        if (MiaoNetModule.Settings.LiveMode)
            return Dialog.Clean("miaonet_chat_disabled");
        var saveData = MiaoNetModule.Instance._SaveData as MiaoNetModuleSaveData;
        var record = saveData?.LastClear;
        if (record is null || !record.IsValid())
            return Dialog.Clean("miaonet_clear_no_record");
        if (saveData!.LastSharedClearID == record.ClearID)
            return Dialog.Clean("miaonet_clear_already_shared");
        long now = Environment.TickCount64;
        if (lastClearShare is long last && now - last < LevelClearRecord.ShareCooldownSeconds * 1000L)
            return Dialog.Clean("miaonet_clear_cooldown");
        context.QueuePacket(new PacketShareLevelClear(record));
        lastClearShare = now;
        return null;
    }
}
