namespace MiaoNet.Shared;

public sealed class WatchSceneSnapshot : IRefBinarySerializable<WatchSceneSnapshot>
{
    public PlayerLocation Location { get; }

    public int Sequence { get; }

    public uint PlayerEpoch { get; }

    public uint PlayerSequenceWatermark { get; }

    public IReadOnlyCollection<string> Flags { get; }

    public IReadOnlyCollection<WatchEntityState> EntityStates { get; }

    public WatchSceneSnapshot(
        PlayerLocation location,
        int sequence,
        IReadOnlyCollection<string> flags,
        IReadOnlyCollection<WatchEntityState> entityStates,
        uint playerEpoch = 0,
        uint playerSequenceWatermark = 0
    )
    {
        Location = location;
        Sequence = sequence;
        PlayerEpoch = playerEpoch;
        PlayerSequenceWatermark = playerSequenceWatermark;
        Flags = flags;
        EntityStates = entityStates;
    }

    public WatchSceneSnapshot(
        PlayerLocation location,
        int sequence,
        IReadOnlyCollection<string> flags
    ) : this(location, sequence, flags, [])
    {
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Location);
        writer.Write(Sequence);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequenceWatermark);
        writer.Write(Flags);
        writer.Write(EntityStates);
    }

    public static WatchSceneSnapshot Deserialize(ref RefBinaryReader reader)
    {
        PlayerLocation location = reader.Read<PlayerLocation>();
        int sequence = reader.ReadInt32();
        uint playerEpoch = reader.ReadUInt32();
        uint playerSequenceWatermark = reader.ReadUInt32();
        return new(
            location,
            sequence,
            reader.ReadStringArray(),
            reader.ReadArray<WatchEntityState>(),
            playerEpoch,
            playerSequenceWatermark
        );
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

    public uint PlayerEpoch { get; }

    public uint PlayerSequenceWatermark { get; }

    public PlayerLocation Location { get; }

    public IReadOnlyCollection<string> AddedFlags { get; }

    public IReadOnlyCollection<string> RemovedFlags { get; }

    public bool RequiresRoomReload { get; }

    public bool IsDeathRespawn { get; }

    public WatchRoomTransition? RoomTransition { get; }

    public WatchEntityStateMode EntityStateMode { get; }

    public IReadOnlyCollection<WatchEntityState> EntityStates { get; }

    public IReadOnlyCollection<WatchEntityEvent> EntityEvents { get; }

    public WatchSceneDelta(
        int sequence,
        PlayerLocation location,
        IReadOnlyCollection<string> addedFlags,
        IReadOnlyCollection<string> removedFlags,
        bool requiresRoomReload,
        WatchEntityStateMode entityStateMode,
        IReadOnlyCollection<WatchEntityState> entityStates,
        IReadOnlyCollection<WatchEntityEvent> entityEvents,
        bool isDeathRespawn = false,
        WatchRoomTransition? roomTransition = null,
        uint playerEpoch = 0,
        uint playerSequenceWatermark = 0
    )
    {
        Sequence = sequence;
        PlayerEpoch = playerEpoch;
        PlayerSequenceWatermark = playerSequenceWatermark;
        Location = location;
        AddedFlags = addedFlags;
        RemovedFlags = removedFlags;
        RequiresRoomReload = requiresRoomReload;
        IsDeathRespawn = isDeathRespawn;
        RoomTransition = roomTransition;
        EntityStateMode = entityStateMode;
        EntityStates = entityStates;
        EntityEvents = entityEvents;
    }

    public WatchSceneDelta(
        int sequence,
        PlayerLocation location,
        IReadOnlyCollection<string> addedFlags,
        IReadOnlyCollection<string> removedFlags,
        bool requiresRoomReload
    ) : this(
        sequence,
        location,
        addedFlags,
        removedFlags,
        requiresRoomReload,
        requiresRoomReload ? WatchEntityStateMode.Replace : WatchEntityStateMode.None,
        [],
        []
    )
    {
    }

    public void Serialize(ref RefBinaryWriter writer)
    {
        writer.Write(Sequence);
        writer.Write(PlayerEpoch);
        writer.Write(PlayerSequenceWatermark);
        writer.Write(Location);
        writer.Write(AddedFlags);
        writer.Write(RemovedFlags);
        writer.Write(RequiresRoomReload);
        writer.Write(IsDeathRespawn);
        writer.Write(RoomTransition.HasValue);
        if (RoomTransition.HasValue)
            writer.Write(RoomTransition.Value);
        writer.Write((byte)EntityStateMode);
        writer.Write(EntityStates);
        writer.Write(EntityEvents);
    }

    public static WatchSceneDelta Deserialize(ref RefBinaryReader reader)
    {
        int sequence = reader.ReadInt32();
        uint playerEpoch = reader.ReadUInt32();
        uint playerSequenceWatermark = reader.ReadUInt32();
        PlayerLocation location = reader.Read<PlayerLocation>();
        string[] addedFlags = reader.ReadStringArray();
        string[] removedFlags = reader.ReadStringArray();
        bool requiresRoomReload = reader.ReadBoolean();
        bool isDeathRespawn = reader.ReadBoolean();
        WatchRoomTransition? roomTransition = reader.ReadBoolean()
            ? reader.Read<WatchRoomTransition>()
            : null;
        WatchEntityStateMode entityStateMode = (WatchEntityStateMode)reader.ReadByte();
        WatchEntityState[] entityStates = reader.ReadArray<WatchEntityState>();
        WatchEntityEvent[] entityEvents = reader.ReadArray<WatchEntityEvent>();
        return new(
            sequence,
            location,
            addedFlags,
            removedFlags,
            requiresRoomReload,
            entityStateMode,
            entityStates,
            entityEvents,
            isDeathRespawn,
            roomTransition,
            playerEpoch,
            playerSequenceWatermark
        );
    }

    public static WatchSceneDelta? Create(
        int sequence,
        PlayerLocation location,
        IReadOnlySet<string> previousFlags,
        IReadOnlySet<string> currentFlags,
        bool requiresRoomReload
    ) => Create(
        sequence,
        location,
        previousFlags,
        currentFlags,
        new Dictionary<WatchEntityKey, WatchEntityState>(),
        new Dictionary<WatchEntityKey, WatchEntityState>(),
        [],
        false,
        requiresRoomReload
    );

    public static WatchSceneDelta? Create(
        int sequence,
        PlayerLocation location,
        IReadOnlySet<string> previousFlags,
        IReadOnlySet<string> currentFlags,
        IReadOnlyDictionary<WatchEntityKey, WatchEntityState> previousEntityStates,
        IReadOnlyDictionary<WatchEntityKey, WatchEntityState> currentEntityStates,
        IReadOnlyCollection<WatchEntityEvent> entityEvents,
        bool forceEntityState,
        bool requiresRoomReload,
        bool isDeathRespawn = false,
        WatchRoomTransition? roomTransition = null,
        uint playerEpoch = 0,
        uint playerSequenceWatermark = 0
    )
    {
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

        return CreateFromChanges(
            sequence,
            location,
            previousFlags,
            currentFlags,
            entityStateMode,
            entityStates,
            entityEvents,
            requiresRoomReload,
            isDeathRespawn,
            roomTransition,
            playerEpoch,
            playerSequenceWatermark
        );
    }

    public static WatchSceneDelta? CreateFromChanges(
        int sequence,
        PlayerLocation location,
        IReadOnlySet<string> previousFlags,
        IReadOnlySet<string> currentFlags,
        WatchEntityStateMode entityStateMode,
        IEnumerable<WatchEntityState> entityStates,
        IReadOnlyCollection<WatchEntityEvent> entityEvents,
        bool requiresRoomReload,
        bool isDeathRespawn = false,
        WatchRoomTransition? roomTransition = null,
        uint playerEpoch = 0,
        uint playerSequenceWatermark = 0
    )
    {
        string[] added = currentFlags.Except(previousFlags, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] removed = previousFlags.Except(currentFlags, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        WatchEntityState[] orderedEntityStates = OrderEntityStates(entityStates);

        return added.Length == 0
            && removed.Length == 0
            && !requiresRoomReload
            && !isDeathRespawn
            && !roomTransition.HasValue
            && entityStateMode == WatchEntityStateMode.None
            && entityEvents.Count == 0
            ? null
            : new(
                sequence,
                location,
                added,
                removed,
                requiresRoomReload,
                entityStateMode,
                orderedEntityStates,
                entityEvents.ToArray(),
                isDeathRespawn,
                roomTransition,
                playerEpoch,
                playerSequenceWatermark
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
