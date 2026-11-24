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
            Logger.Error(LT.MiaoNet, "Failed to persist shared level clear state.");
            Logger.LogDetailed(e, LT.MiaoNet);
        }
    }
}
