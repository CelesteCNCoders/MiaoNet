namespace MiaoNet.Shared;

public sealed class WatchSceneSnapshot : IRefBinarySerializable<WatchSceneSnapshot>
{
    public PlayerLocation Location { get; }

    public int Sequence { get; }

    public IReadOnlyCollection<string> Flags { get; }

    public IReadOnlyCollection<int> ActiveTouchSwitchIDs { get; }

    public IReadOnlyCollection<WatchEntityState> EntityStates { get; }

    public WatchSceneSnapshot(
        PlayerLocation location,
        int sequence,
        IReadOnlyCollection<string> flags,
        IReadOnlyCollection<int> activeTouchSwitchIDs,
        IReadOnlyCollection<WatchEntityState> entityStates
    )
    {
        Location = location;
        Sequence = sequence;
        Flags = flags;
        ActiveTouchSwitchIDs = activeTouchSwitchIDs;
        EntityStates = entityStates;
    }

    public WatchSceneSnapshot(
        PlayerLocation location,
        int sequence,
        IReadOnlyCollection<string> flags,
        IReadOnlyCollection<int> activeTouchSwitchIDs
    ) : this(location, sequence, flags, activeTouchSwitchIDs, [])
    {
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);
        writer.Write(Sequence);
        writer.Write(Flags);
        WriteIDs(ref writer, ActiveTouchSwitchIDs);
        writer.Write(EntityStates);
    }

    public static WatchSceneSnapshot Deserialize(ref RefBinaryReader reader)
        => new(
            reader.Read<PlayerLocation>(),
            reader.ReadInt32(),
            reader.ReadStringArray(),
            ReadIDs(ref reader),
            reader.ReadArray<WatchEntityState>()
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

public static class WatchSceneLifecyclePolicy
{
    public static bool AuthorizesRoomReload(WatchSceneDelta delta)
        => delta.RequiresRoomReload && !delta.IsDeathRespawn;
}

public sealed class WatchSceneDelta : IRefBinarySerializable<WatchSceneDelta>
{
    public int Sequence { get; }

    public PlayerLocation Location { get; }

    public IReadOnlyCollection<string> AddedFlags { get; }

    public IReadOnlyCollection<string> RemovedFlags { get; }

    public bool RequiresRoomReload { get; }

    public bool IsDeathRespawn { get; }

    public WatchRoomTransition? RoomTransition { get; }

    public bool HasTouchSwitchState { get; }

    public IReadOnlyCollection<int> ActiveTouchSwitchIDs { get; }

    public WatchEntityStateMode EntityStateMode { get; }

    public IReadOnlyCollection<WatchEntityState> EntityStates { get; }

    public IReadOnlyCollection<WatchEntityEvent> EntityEvents { get; }

    public WatchSceneDelta(
        int sequence,
        PlayerLocation location,
        IReadOnlyCollection<string> addedFlags,
        IReadOnlyCollection<string> removedFlags,
        bool requiresRoomReload,
        bool hasTouchSwitchState,
        IReadOnlyCollection<int> activeTouchSwitchIDs,
        WatchEntityStateMode entityStateMode,
        IReadOnlyCollection<WatchEntityState> entityStates,
        IReadOnlyCollection<WatchEntityEvent> entityEvents,
        bool isDeathRespawn = false,
        WatchRoomTransition? roomTransition = null
    )
    {
        Sequence = sequence;
        Location = location;
        AddedFlags = addedFlags;
        RemovedFlags = removedFlags;
        RequiresRoomReload = requiresRoomReload;
        IsDeathRespawn = isDeathRespawn;
        RoomTransition = roomTransition;
        HasTouchSwitchState = hasTouchSwitchState;
        ActiveTouchSwitchIDs = activeTouchSwitchIDs;
        EntityStateMode = entityStateMode;
        EntityStates = entityStates;
        EntityEvents = entityEvents;
    }

    public WatchSceneDelta(
        int sequence,
        PlayerLocation location,
        IReadOnlyCollection<string> addedFlags,
        IReadOnlyCollection<string> removedFlags,
        bool requiresRoomReload,
        bool hasTouchSwitchState,
        IReadOnlyCollection<int> activeTouchSwitchIDs
    ) : this(
        sequence,
        location,
        addedFlags,
        removedFlags,
        requiresRoomReload,
        hasTouchSwitchState,
        activeTouchSwitchIDs,
        requiresRoomReload ? WatchEntityStateMode.Replace : WatchEntityStateMode.None,
        [],
        []
    )
    {
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Sequence);
        writer.Write(Location);
        writer.Write(AddedFlags);
        writer.Write(RemovedFlags);
        writer.Write(RequiresRoomReload);
        writer.Write(IsDeathRespawn);
        writer.Write(RoomTransition.HasValue);
        if (RoomTransition.HasValue)
            writer.Write(RoomTransition.Value);
        writer.Write(HasTouchSwitchState);
        if (HasTouchSwitchState)
            WatchSceneSnapshot.WriteIDs(ref writer, ActiveTouchSwitchIDs);
        writer.Write((byte)EntityStateMode);
        writer.Write(EntityStates);
        writer.Write(EntityEvents);
    }

    public static WatchSceneDelta Deserialize(ref RefBinaryReader reader)
    {
        int sequence = reader.ReadInt32();
        PlayerLocation location = reader.Read<PlayerLocation>();
        string[] addedFlags = reader.ReadStringArray();
        string[] removedFlags = reader.ReadStringArray();
        bool requiresRoomReload = reader.ReadBoolean();
        bool isDeathRespawn = reader.ReadBoolean();
        WatchRoomTransition? roomTransition = reader.ReadBoolean()
            ? reader.Read<WatchRoomTransition>()
            : null;
        bool hasTouchSwitchState = reader.ReadBoolean();
        int[] activeTouchSwitchIDs = hasTouchSwitchState
            ? WatchSceneSnapshot.ReadIDs(ref reader)
            : [];
        WatchEntityStateMode entityStateMode = (WatchEntityStateMode)reader.ReadByte();
        WatchEntityState[] entityStates = reader.ReadArray<WatchEntityState>();
        WatchEntityEvent[] entityEvents = reader.ReadArray<WatchEntityEvent>();
        return new(
            sequence,
            location,
            addedFlags,
            removedFlags,
            requiresRoomReload,
            hasTouchSwitchState,
            activeTouchSwitchIDs,
            entityStateMode,
            entityStates,
            entityEvents,
            isDeathRespawn,
            roomTransition
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
    ) => Create(
        sequence,
        location,
        previousFlags,
        currentFlags,
        previousTouchSwitchIDs,
        currentTouchSwitchIDs,
        new Dictionary<WatchEntityKey, WatchEntityState>(),
        new Dictionary<WatchEntityKey, WatchEntityState>(),
        [],
        forceTouchSwitchState,
        false,
        requiresRoomReload
    );

    public static WatchSceneDelta? Create(
        int sequence,
        PlayerLocation location,
        IReadOnlySet<string> previousFlags,
        IReadOnlySet<string> currentFlags,
        IReadOnlySet<int> previousTouchSwitchIDs,
        IReadOnlySet<int> currentTouchSwitchIDs,
        IReadOnlyDictionary<WatchEntityKey, WatchEntityState> previousEntityStates,
        IReadOnlyDictionary<WatchEntityKey, WatchEntityState> currentEntityStates,
        IReadOnlyCollection<WatchEntityEvent> entityEvents,
        bool forceTouchSwitchState,
        bool forceEntityState,
        bool requiresRoomReload,
        bool isDeathRespawn = false,
        WatchRoomTransition? roomTransition = null
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
        WatchEntityStateMode entityStateMode;
        WatchEntityState[] entityStates;
        if (requiresRoomReload
            || forceEntityState
            || previousEntityStates.Keys.Any(key => !currentEntityStates.ContainsKey(key)))
        {
            entityStateMode = WatchEntityStateMode.Replace;
            entityStates = OrderEntityStates(currentEntityStates.Values);
        }
        else
        {
            entityStates = OrderEntityStates(currentEntityStates.Values.Where(state =>
                !previousEntityStates.TryGetValue(state.Key, out WatchEntityState previous)
                || !state.Payload.Span.SequenceEqual(previous.Payload.Span)
            ));
            entityStateMode = entityStates.Length == 0
                ? WatchEntityStateMode.None
                : WatchEntityStateMode.Patch;
        }

        return added.Length == 0
            && removed.Length == 0
            && !requiresRoomReload
            && !isDeathRespawn
            && !roomTransition.HasValue
            && !hasTouchSwitchState
            && entityStateMode == WatchEntityStateMode.None
            && entityEvents.Count == 0
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
                    : [],
                entityStateMode,
                entityStates,
                entityEvents.ToArray(),
                isDeathRespawn,
                roomTransition
            );
    }

    internal static WatchEntityState[] OrderEntityStates(IEnumerable<WatchEntityState> states)
        => states.OrderBy(state => state.Key.Kind)
            .ThenBy(state => state.Key.EntityID)
            .ThenBy(state => state.Key.SubID)
            .ToArray();

    public void ApplyTo(ISet<string> flags)
    {
        foreach (string flag in RemovedFlags)
            flags.Remove(flag);
        foreach (string flag in AddedFlags)
            flags.Add(flag);
    }
}
