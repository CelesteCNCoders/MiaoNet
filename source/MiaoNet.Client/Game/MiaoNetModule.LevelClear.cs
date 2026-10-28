// 收到服务器回显后，持久化已分享状态；换存档不误标记其他存档。
namespace Celeste.Mod.MiaoNet;

public sealed partial class MiaoNetModule
{
    internal void ConfirmClearShared(string clearID)
    {
        if (_SaveData is not MiaoNetModuleSaveData data || data.LastClear?.ClearID != clearID)
            return;
        if (data.LastSharedClearID == clearID)
            return;
        data.LastSharedClearID = clearID;
        try
        {
            WriteSaveData(data.Index, SerializeSaveData(data.Index));
        }
        catch (Exception e)
        {
            Logger.Error(LT.MiaoNet, "无法保存通关分享状态。");
            Logger.LogDetailed(e, LT.MiaoNet);
        }
    }
}
