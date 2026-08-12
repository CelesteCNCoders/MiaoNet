using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

public sealed class TeleportInfo
{
    public readonly bool MoveToDebugSave;
    public readonly PlayerSessionData? SessionData;
    public readonly AreaKey AreaKey;
    public readonly string MapRoom;

    public TeleportInfo(bool moveToDebugSave, PlayerSessionData? sessionData, AreaKey areaKey, string mapRoom)
    {
        MoveToDebugSave = moveToDebugSave;
        SessionData = sessionData;
        AreaKey = areaKey;
        MapRoom = mapRoom;
    }
}
