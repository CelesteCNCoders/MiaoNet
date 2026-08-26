using System.Text;

namespace MiaoNet.Shared;

public static class WatchPacketValidator
{
    public const int MaxFlagCount = 4096;
    public const int MaxFlagUtf8Bytes = 1024;
    public const int MaxTouchSwitchCount = 4096;

    private const int MaxSnapshotFlagsSize = 30_000;

    public static bool IsValid(WatchSceneSnapshot snapshot)
    {
        if (!snapshot.Location.IsInMap
            || snapshot.Sequence < 0
            || !TryGetFlagsSerializedSize(snapshot.Flags, out int flagsSize)
            || flagsSize > MaxSnapshotFlagsSize
            || !TryGetTouchSwitchesSerializedSize(snapshot.ActiveTouchSwitchIDs, out int switchesSize))
            return false;

        long packetSize = sizeof(int) + sizeof(byte)
            + GetLocationSerializedSize(snapshot.Location)
            + sizeof(int) + flagsSize + switchesSize;
        return packetSize <= Connection.MaxPayloadSize;
    }

    public static bool IsValid(WatchSceneDelta delta)
    {
        if (!delta.Location.IsInMap
            || delta.Sequence <= 0
            || (delta.AddedFlags.Count == 0
                && delta.RemovedFlags.Count == 0
                && !delta.RequiresRoomReload
                && !delta.HasTouchSwitchState)
            || (long)delta.AddedFlags.Count + delta.RemovedFlags.Count > MaxFlagCount
            || !TryGetFlagsSerializedSize(delta.AddedFlags, out int addedSize)
            || !TryGetFlagsSerializedSize(delta.RemovedFlags, out int removedSize)
            || (delta.HasTouchSwitchState
                && !TryGetTouchSwitchesSerializedSize(delta.ActiveTouchSwitchIDs, out _))
            || (delta.RequiresRoomReload && !delta.HasTouchSwitchState)
            || (!delta.HasTouchSwitchState && delta.ActiveTouchSwitchIDs.Count != 0))
            return false;

        HashSet<string> added = new(delta.AddedFlags, StringComparer.Ordinal);
        if (delta.RemovedFlags.Any(added.Contains))
            return false;

        int switchesSize = delta.HasTouchSwitchState
            ? sizeof(ushort) + sizeof(int) * delta.ActiveTouchSwitchIDs.Count
            : 0;
        long notificationSize = sizeof(int) * 3L
            + GetLocationSerializedSize(delta.Location)
            + addedSize + removedSize
            + sizeof(bool) * 2L + switchesSize;
        return notificationSize <= Connection.MaxPayloadSize;
    }

    private static bool TryGetTouchSwitchesSerializedSize(
        IReadOnlyCollection<int> ids,
        out int serializedSize
    )
    {
        serializedSize = sizeof(ushort) + sizeof(int) * ids.Count;
        return ids.Count <= MaxTouchSwitchCount
            && ids.All(id => id >= 0)
            && ids.Distinct().Count() == ids.Count;
    }

    private static bool TryGetFlagsSerializedSize(
        IReadOnlyCollection<string> flags,
        out int serializedSize
    )
    {
        serializedSize = sizeof(ushort);
        if (flags.Count > MaxFlagCount)
            return false;

        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string flag in flags)
        {
            int byteCount = Encoding.UTF8.GetByteCount(flag);
            if (byteCount > MaxFlagUtf8Bytes || !unique.Add(flag))
                return false;

            serializedSize += sizeof(ushort) + byteCount;
        }
        return true;
    }

    private static int GetLocationSerializedSize(PlayerLocation location)
    {
        int size = sizeof(ushort) + Encoding.UTF8.GetByteCount(location.Map.Sid);
        if (!location.Map.IsEmpty)
        {
            size += sizeof(byte);
            size += sizeof(ushort) + Encoding.UTF8.GetByteCount(location.Room);
        }
        return size;
    }
}
