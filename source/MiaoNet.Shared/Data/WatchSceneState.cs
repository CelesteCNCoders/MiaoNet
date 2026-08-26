namespace MiaoNet.Shared;

public sealed class WatchSceneSnapshot : IRefBinarySerializable<WatchSceneSnapshot>
{
    public PlayerLocation Location { get; }

    public int Sequence { get; }

    public IReadOnlyCollection<string> Flags { get; }

    public IReadOnlyCollection<int> ActiveTouchSwitchIDs { get; }

    public WatchSceneSnapshot(
        PlayerLocation location,
        int sequence,
        IReadOnlyCollection<string> flags,
        IReadOnlyCollection<int> activeTouchSwitchIDs
    )
    {
        Location = location;
        Sequence = sequence;
        Flags = flags;
        ActiveTouchSwitchIDs = activeTouchSwitchIDs;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);
        writer.Write(Sequence);
        writer.Write(Flags);
        WriteIDs(ref writer, ActiveTouchSwitchIDs);
    }

    public static WatchSceneSnapshot Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerLocation>(),
            reader.ReadInt32(),
            reader.ReadStringArray(),
            ReadIDs(ref reader)
        );

    internal static void WriteIDs(ref RefBinaryWriter writer, IReadOnlyCollection<int> ids)
    {
        if (ids.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(ids));

        writer.Write((ushort)ids.Count);
        foreach (int id in ids)
            writer.Write(id);
    }

    internal static int[] ReadIDs(ref RefBinaryReader reader)
    {
        int count = reader.ReadUInt16();
        int[] ids = new int[count];
        for (int i = 0; i < count; i++)
            ids[i] = reader.ReadInt32();
        return ids;
    }
}

public sealed class WatchSceneDelta : IRefBinarySerializable<WatchSceneDelta>
{
    public int Sequence { get; }

    public PlayerLocation Location { get; }

    public IReadOnlyCollection<string> AddedFlags { get; }

    public IReadOnlyCollection<string> RemovedFlags { get; }

    public bool RequiresRoomReload { get; }

    public bool HasTouchSwitchState { get; }

    public IReadOnlyCollection<int> ActiveTouchSwitchIDs { get; }

    public WatchSceneDelta(
        int sequence,
        PlayerLocation location,
        IReadOnlyCollection<string> addedFlags,
        IReadOnlyCollection<string> removedFlags,
        bool requiresRoomReload,
        bool hasTouchSwitchState,
        IReadOnlyCollection<int> activeTouchSwitchIDs
    )
    {
        Sequence = sequence;
        Location = location;
        AddedFlags = addedFlags;
        RemovedFlags = removedFlags;
        RequiresRoomReload = requiresRoomReload;
        HasTouchSwitchState = hasTouchSwitchState;
        ActiveTouchSwitchIDs = activeTouchSwitchIDs;
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Sequence);
        writer.Write(Location);
        writer.Write(AddedFlags);
        writer.Write(RemovedFlags);
        writer.Write(RequiresRoomReload);
        writer.Write(HasTouchSwitchState);
        if (HasTouchSwitchState)
            WatchSceneSnapshot.WriteIDs(ref writer, ActiveTouchSwitchIDs);
    }

    public static WatchSceneDelta Deserialize(ref RefBinaryReader reader)
    {
        int sequence = reader.ReadInt32();
        PlayerLocation location = reader.Read<PlayerLocation>();
        string[] addedFlags = reader.ReadStringArray();
        string[] removedFlags = reader.ReadStringArray();
        bool requiresRoomReload = reader.ReadBoolean();
        bool hasTouchSwitchState = reader.ReadBoolean();
        int[] activeTouchSwitchIDs = hasTouchSwitchState
            ? WatchSceneSnapshot.ReadIDs(ref reader)
            : [];
        return new(
            sequence,
            location,
            addedFlags,
            removedFlags,
            requiresRoomReload,
            hasTouchSwitchState,
            activeTouchSwitchIDs
        );
    }

    public static WatchSceneDelta? Create(
        int sequence,
        PlayerLocation location,
        IReadOnlySet<string> previousFlags,
        IReadOnlySet<string> currentFlags,
        IReadOnlySet<int> previousTouchSwitchIDs,
        IReadOnlySet<int> currentTouchSwitchIDs,
        bool forceTouchSwitchState,
        bool requiresRoomReload
    )
    {
        string[] added = currentFlags.Except(previousFlags, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] removed = previousFlags.Except(currentFlags, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        bool hasTouchSwitchState = requiresRoomReload
            || forceTouchSwitchState
            || !previousTouchSwitchIDs.SetEquals(currentTouchSwitchIDs);

        return added.Length == 0
            && removed.Length == 0
            && !requiresRoomReload
            && !hasTouchSwitchState
            ? null
            : new(
                sequence,
                location,
                added,
                removed,
                requiresRoomReload,
                hasTouchSwitchState,
                hasTouchSwitchState
                    ? currentTouchSwitchIDs.Order().ToArray()
                    : []
            );
    }

    public void ApplyTo(ISet<string> flags)
    {
        foreach (string flag in RemovedFlags)
            flags.Remove(flag);
        foreach (string flag in AddedFlags)
            flags.Add(flag);
    }
}
