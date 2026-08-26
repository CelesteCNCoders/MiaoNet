using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchCassetteBlockAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte ManagerType = 0;
    private const byte BlockType = 1;
    private static readonly WatchCassetteBlockAdapter instance = new();
    private static readonly Dictionary<WatchEntityKey, byte[]> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.CassetteBlock;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.CassetteBlockManager.Update += CassetteBlockManager_Update;
        On.Celeste.CassetteBlock.ctor_EntityData_Vector2_EntityID += CassetteBlock_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.CassetteBlock.ctor_EntityData_Vector2_EntityID -= CassetteBlock_ctor;
        On.Celeste.CassetteBlockManager.Update -= CassetteBlockManager_Update;
        On.Celeste.Level.Update -= Level_Update;
        WatchEntityIDTable<CassetteBlock>.Clear();
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        CassetteBlockManager? manager = level.Tracker.GetEntity<CassetteBlockManager>();
        if (manager is not null)
        {
            var current = (
                manager.currentIndex,
                manager.beatTimer,
                manager.beatIndex,
                manager.tempoMult,
                manager.beatIndexOffset
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, 0), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = ManagerType;
                    WatchEntityPayloadCodec.WriteInt32(payload, 4, state.currentIndex);
                    WatchEntityPayloadCodec.WriteSingle(payload, 8, state.beatTimer);
                    WatchEntityPayloadCodec.WriteInt32(payload, 12, state.beatIndex);
                    WatchEntityPayloadCodec.WriteSingle(payload, 16, state.tempoMult);
                    WatchEntityPayloadCodec.WriteInt32(payload, 20, state.beatIndexOffset);
                }
            );
        }

        string room = level.Session.Level;
        foreach (CassetteBlock block in WatchRoomEntityIndex.Enumerate<CassetteBlock>(level))
        {
            if (!WatchEntityIDTable<CassetteBlock>.TryGet(block, room, out int id))
                continue;
            byte flags = (byte)((block.Activated ? 1 : 0)
                | (block.Visible ? 2 : 0)
                | (block.Collidable ? 4 : 0));
            var current = (Flags: flags, Mode: (byte)block.Mode, Index: (byte)block.Index,
                block.Position, Height: block.blockHeight);
            yield return WatchEntityState.FromTyped(
                new(Kind, id, 1), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = BlockType;
                    payload[1] = state.Flags;
                    payload[2] = state.Mode;
                    payload[3] = state.Index;
                    WatchEntityPayloadCodec.WriteVector2(payload, 4, state.Position);
                    WatchEntityPayloadCodec.WriteInt32(payload, 12, state.Height);
                }
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        string room = level.Session.Level;
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteStates.Clear();
            remoteRoom = room;
        }

        HashSet<WatchEntityKey> packetKeys = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !packetKeys.Add(state.Key))
                return WatchEntityApplyResult.None;
            remoteStates[state.Key] = state.Payload.ToArray();
        }

        bool changed = ApplyRemote(level);
        bool requiresReload = false;
        if (isCompleteState)
        {
            HashSet<WatchEntityKey> localKeys = EnumerateKeys(level).ToHashSet();
            requiresReload = remoteStates.Keys.Any(key => !localKeys.Contains(key))
                || localKeys.Any(key => !remoteStates.ContainsKey(key));
        }
        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }


    private static bool ApplyRemote(Level level)
    {
        bool changed = false;
        WatchEntityKey managerKey = new(WatchEntityKind.CassetteBlock, 0);
        CassetteBlockManager? manager = level.Tracker.GetEntity<CassetteBlockManager>();
        if (manager is not null && remoteStates.TryGetValue(managerKey, out byte[]? managerPayload))
            changed |= ApplyRemoteManager(manager, managerPayload);

        string room = level.Session.Level;
        foreach (CassetteBlock block in WatchRoomEntityIndex.Enumerate<CassetteBlock>(level))
        {
            if (!WatchEntityIDTable<CassetteBlock>.TryGet(block, room, out int id)
                || !remoteStates.TryGetValue(
                    new WatchEntityKey(WatchEntityKind.CassetteBlock, id, 1),
                    out byte[]? payload
                ))
                continue;
            bool activated = (payload[1] & 1) != 0;
            bool visible = (payload[1] & 2) != 0;
            bool collidable = (payload[1] & 4) != 0;
            CassetteBlock.Modes mode = (CassetteBlock.Modes)payload[2];
            Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
            int blockHeight = WatchEntityPayloadCodec.ReadInt32(payload, 12);
            bool collidableChanged = block.Collidable != collidable;
            changed |= block.Activated != activated
                || block.Visible != visible
                || collidableChanged
                || block.Mode != mode
                || block.Position != position
                || block.blockHeight != blockHeight;
            if (block.Position != position)
            {
                Vector2 movement = position - block.Position;
                block.Position = position;
                block.MoveStaticMovers(movement);
                block.ClearRemainder();
            }
            block.Activated = activated;
            block.Collidable = collidable;
            block.Mode = mode;
            block.blockHeight = blockHeight;
            if (collidableChanged)
            {
                if (collidable)
                    block.EnableStaticMovers();
                else
                    block.DisableStaticMovers();
            }
            block.UpdateVisualState();
            block.Visible = visible;
        }
        return changed;
    }

    private static bool ApplyRemoteManager(CassetteBlockManager manager, byte[] payload)
    {
        int currentIndex = WatchEntityPayloadCodec.ReadInt32(payload, 4);
        float beatTimer = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        int beatIndex = WatchEntityPayloadCodec.ReadInt32(payload, 12);
        float tempoMult = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        int offset = WatchEntityPayloadCodec.ReadInt32(payload, 20);
        bool changed = manager.currentIndex != currentIndex
            || manager.beatTimer != beatTimer
            || manager.beatIndex != beatIndex
            || manager.tempoMult != tempoMult
            || manager.beatIndexOffset != offset;
        manager.currentIndex = currentIndex;
        manager.beatTimer = beatTimer;
        manager.beatIndex = beatIndex;
        manager.tempoMult = tempoMult;
        manager.beatIndexOffset = offset;
        return changed;
    }

    private static IEnumerable<WatchEntityKey> EnumerateKeys(Level level)
    {
        if (level.Tracker.GetEntity<CassetteBlockManager>() is not null)
            yield return new WatchEntityKey(WatchEntityKind.CassetteBlock, 0);
        string room = level.Session.Level;
        foreach (CassetteBlock block in WatchRoomEntityIndex.Enumerate<CassetteBlock>(level))
        {
            if (WatchEntityIDTable<CassetteBlock>.TryGet(block, room, out int id))
                yield return new WatchEntityKey(WatchEntityKind.CassetteBlock, id, 1);
        }
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.CassetteBlock || payload.Length != PayloadSize)
            return false;
        if (payload[0] == ManagerType)
            return state.Key.EntityID == 0 && state.Key.SubID == 0
                && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
                && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 16));
        return payload[0] == BlockType
            && state.Key.SubID == 1
            && (payload[1] & ~7) == 0
            && payload[2] <= (byte)CassetteBlock.Modes.Returning
            && payload[3] <= 3
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 4))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && WatchEntityPayloadCodec.ReadInt32(payload, 12) is >= 0
                and <= WatchPacketValidator.MaxCassetteBlockHeight
            && payload[16..].IndexOfAnyExcept((byte)0) < 0;
    }

    private static void CassetteBlockManager_Update(
        On.Celeste.CassetteBlockManager.orig_Update orig,
        CassetteBlockManager self
    )
    {
        WatchEntityKey key = new(WatchEntityKind.CassetteBlock, 0);
        if (MiaoNetModule.IsWatching
            && self.Scene is Level level
            && StringComparer.Ordinal.Equals(remoteRoom, level.Session.Level)
            && remoteStates.TryGetValue(key, out byte[]? payload))
        {
            ApplyRemoteManager(self, payload);
            return;
        }
        orig(self);
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if (MiaoNetModule.IsWatching
            && StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            ApplyRemote(self);
    }

    private static void CassetteBlock_ctor(
        On.Celeste.CassetteBlock.orig_ctor_EntityData_Vector2_EntityID orig,
        CassetteBlock self,
        EntityData data,
        Vector2 offset,
        EntityID id
    )
    {
        orig(self, data, offset, id);
        WatchEntityIDTable<CassetteBlock>.Set(self, data.Level.Name, data.ID);
    }
}

internal sealed class WatchTouchSwitchAndSwitchGateAdapter : IWatchEntityAdapter
{
    private const int SwitchGatePayloadSize = 20;
    private const ushort SwitchGateSubID = 0;
    private const ushort TouchSwitchSubID = 1;
    private const int TouchSwitchStateEntityID = 0;

    private static readonly WatchTouchSwitchAndSwitchGateAdapter instance = new();
    private static readonly Dictionary<int, byte[]> remoteSwitchGateStates = new();
    private static readonly HashSet<int> remoteActiveTouchSwitchIDs = new();
    private static bool hasRemoteTouchSwitchState;
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.TouchSwitchAndSwitchGate;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.TouchSwitch.ctor_EntityData_Vector2 += TouchSwitch_ctor;
        On.Celeste.SwitchGate.ctor_EntityData_Vector2 += SwitchGate_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.SwitchGate.ctor_EntityData_Vector2 -= SwitchGate_ctor;
        On.Celeste.TouchSwitch.ctor_EntityData_Vector2 -= TouchSwitch_ctor;
        On.Celeste.Level.Update -= Level_Update;
        WatchEntityIDTable<SwitchGate>.Clear();
        WatchEntityIDTable<TouchSwitch>.Clear();
        remoteSwitchGateStates.Clear();
        remoteActiveTouchSwitchIDs.Clear();
        hasRemoteTouchSwitchState = false;
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        int[] activeTouchSwitchIDs = WatchRoomEntityIndex.Enumerate<TouchSwitch>(level)
            .Where(touchSwitch => touchSwitch.Switch.Activated)
            .Select(touchSwitch => WatchEntityIDTable<TouchSwitch>.TryGet(
                touchSwitch,
                room,
                out int id
            ) ? id : -1)
            .Where(id => id >= 0)
            .Order()
            .ToArray();
        yield return WatchEntityState.FromTyped(
            new(Kind, TouchSwitchStateEntityID, TouchSwitchSubID),
            activeTouchSwitchIDs,
            static ids =>
            {
                byte[] payload = new byte[sizeof(int) * ids.Length];
                for (int index = 0; index < ids.Length; index++)
                    WatchEntityPayloadCodec.WriteInt32(payload, sizeof(int) * index, ids[index]);
                return payload;
            },
            WatchArrayEqualityComparer<int>.Instance
        );

        foreach (SwitchGate gate in WatchRoomEntityIndex.Enumerate<SwitchGate>(level))
        {
            if (!WatchEntityIDTable<SwitchGate>.TryGet(gate, room, out int id))
                continue;
            byte flags = (byte)((gate.Visible ? 1 : 0)
                | (gate.Collidable ? 2 : 0)
                | (gate.persistent ? 4 : 0));
            var current = (
                Flags: flags,
                Animation: EncodeAnimation(gate.icon.CurrentAnimationID),
                AnimationFrame: (ushort)Math.Max(0, gate.icon.CurrentAnimationFrame),
                gate.Position,
                Wiggle: gate.wiggler.Value,
                Rotation: gate.icon.Rotation
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id, SwitchGateSubID), current, SwitchGatePayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.Animation;
                    WatchEntityPayloadCodec.WriteUInt16(payload, 2, state.AnimationFrame);
                    WatchEntityPayloadCodec.WriteVector2(payload, 4, state.Position);
                    WatchEntityPayloadCodec.WriteSingle(payload, 12, state.Wiggle);
                    WatchEntityPayloadCodec.WriteSingle(payload, 16, state.Rotation);
                }
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        string room = level.Session.Level;
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteSwitchGateStates.Clear();
            remoteActiveTouchSwitchIDs.Clear();
            hasRemoteTouchSwitchState = false;
            remoteRoom = room;
        }
        HashSet<WatchEntityKey> keys = new();
        bool touchSwitchStateUpdated = false;
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !keys.Add(state.Key))
                return WatchEntityApplyResult.None;

            if (state.Key.SubID == TouchSwitchSubID)
            {
                remoteActiveTouchSwitchIDs.Clear();
                hasRemoteTouchSwitchState = true;
                touchSwitchStateUpdated = true;
                ReadOnlySpan<byte> payload = state.Payload.Span;
                for (int offset = 0; offset < payload.Length; offset += sizeof(int))
                    remoteActiveTouchSwitchIDs.Add(
                        WatchEntityPayloadCodec.ReadInt32(payload, offset)
                    );
            }
            else
            {
                remoteSwitchGateStates[state.Key.EntityID] = state.Payload.ToArray();
            }
        }

        WatchEntityApplyResult touchSwitchResult = touchSwitchStateUpdated
            ? ApplyRemoteTouchSwitches(level, isCompleteState)
            : WatchEntityApplyResult.None;
        bool changed = ApplyRemoteSwitchGates(level);
        HashSet<int> local = WatchRoomEntityIndex.Enumerate<SwitchGate>(level)
            .Select(gate => WatchEntityIDTable<SwitchGate>.TryGet(gate, room, out int id) ? id : -1)
            .Where(id => id >= 0).ToHashSet();
        bool switchGateMismatch = isCompleteState
            && (remoteSwitchGateStates.Keys.Any(id => !local.Contains(id))
                || local.Any(id => !remoteSwitchGateStates.ContainsKey(id)));
        return touchSwitchResult
            | (changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None)
            | (switchGateMismatch
                ? WatchEntityApplyResult.RequiresRoomReload
                : WatchEntityApplyResult.None);
    }

    private static WatchEntityApplyResult ApplyRemoteTouchSwitches(Level level, bool isCompleteState)
    {
        if (!hasRemoteTouchSwitchState)
            return WatchEntityApplyResult.None;

        string room = level.Session.Level;
        TouchSwitch[] current = WatchRoomEntityIndex.Enumerate<TouchSwitch>(level).ToArray();
        HashSet<int> mapIDs = level.Session.LevelData.Entities
            .Where(data => data.Name == "touchSwitch")
            .Select(data => data.ID)
            .ToHashSet();
        HashSet<int> currentIDs = GetTouchSwitchIDs(current, room);
        bool staleActivation = current.Any(touchSwitch =>
            touchSwitch.Switch.Activated
            && WatchEntityIDTable<TouchSwitch>.TryGet(touchSwitch, room, out int id)
            && !remoteActiveTouchSwitchIDs.Contains(id)
        );
        bool entitySetMismatch = !mapIDs.SetEquals(currentIDs);
        int recreatedCount = 0;
        bool changed = false;
        bool incomplete = false;

        if (WatchEntitySyncRegistry.IsApplyingLifecycleReset
            && (staleActivation || entitySetMismatch))
        {
            if (TryRecreateTouchSwitches(level, current, mapIDs.Count, out recreatedCount))
            {
                changed = recreatedCount > 0;
                current = WatchRoomEntityIndex.Enumerate<TouchSwitch>(level).ToArray();
                currentIDs = GetTouchSwitchIDs(current, room);
                staleActivation = false;
                entitySetMismatch = !mapIDs.SetEquals(currentIDs);
            }
            else
            {
                incomplete = true;
            }
        }

        HashSet<int> missingIDs = new(remoteActiveTouchSwitchIDs);
        int activatedCount = 0;
        foreach (TouchSwitch touchSwitch in current)
        {
            if (!WatchEntityIDTable<TouchSwitch>.TryGet(touchSwitch, room, out int id)
                || !remoteActiveTouchSwitchIDs.Contains(id))
                continue;

            missingIDs.Remove(id);
            if (!touchSwitch.Switch.Activated)
            {
                touchSwitch.TurnOn();
                activatedCount++;
            }
        }

        changed |= activatedCount > 0;
        incomplete |= missingIDs.Count > 0
            || (isCompleteState && (staleActivation || entitySetMismatch));
        if (changed || incomplete)
        {
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Applied TouchSwitch watch state for room {room}; " +
                $"requested={remoteActiveTouchSwitchIDs.Count}, activated={activatedCount}, " +
                $"recreated={recreatedCount}, missing={missingIDs.Count}, " +
                $"incomplete={incomplete}."
            );
        }

        return (changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None)
            | (incomplete
                ? WatchEntityApplyResult.RequiresRoomReload
                : WatchEntityApplyResult.None);
    }

    private static HashSet<int> GetTouchSwitchIDs(
        IEnumerable<TouchSwitch> touchSwitches,
        string room
    ) => touchSwitches
        .Select(touchSwitch => WatchEntityIDTable<TouchSwitch>.TryGet(
            touchSwitch,
            room,
            out int id
        ) ? id : -1)
        .Where(id => id >= 0)
        .ToHashSet();

    private static bool TryRecreateTouchSwitches(
        Level level,
        IReadOnlyCollection<TouchSwitch> current,
        int expectedCount,
        out int recreatedCount
    )
    {
        recreatedCount = 0;
        LevelData levelData = level.Session.LevelData;
        EntityData[] data = levelData.Entities
            .Where(entity => entity.Name == "touchSwitch")
            .ToArray();
        if (data.Length != expectedCount)
            return false;

        foreach (TouchSwitch touchSwitch in current)
        {
            touchSwitch.Visible = false;
            touchSwitch.RemoveSelf();
        }
        level.Entities.UpdateLists();

        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        foreach (EntityData entityData in data)
        {
            level.Add(new TouchSwitch(entityData, offset));
            recreatedCount++;
        }
        level.Entities.UpdateLists();
        return recreatedCount == expectedCount;
    }

    private static bool ApplyRemoteSwitchGates(Level level)
    {
        bool changed = false;
        string room = level.Session.Level;
        foreach (SwitchGate gate in WatchRoomEntityIndex.Enumerate<SwitchGate>(level))
        {
            if (!WatchEntityIDTable<SwitchGate>.TryGet(gate, room, out int id)
                || !remoteSwitchGateStates.TryGetValue(id, out byte[]? payload))
                continue;
            bool visible = (payload[0] & 1) != 0;
            bool collidable = (payload[0] & 2) != 0;
            Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
            byte animation = payload[1];
            int frame = WatchEntityPayloadCodec.ReadUInt16(payload, 2);
            changed |= gate.Visible != visible || gate.Collidable != collidable || gate.Position != position;
            if (gate.Position != position)
            {
                Vector2 movement = position - gate.Position;
                gate.Position = position;
                gate.MoveStaticMovers(movement);
                gate.ClearRemainder();
            }
            gate.Visible = visible;
            gate.Collidable = collidable;
            string? animationID = DecodeAnimation(animation);
            if (animationID is not null && gate.icon.CurrentAnimationID != animationID)
                gate.icon.Play(animationID);
            if (animationID is not null)
                gate.icon.SetAnimationFrame(frame);
            gate.wiggler.Value = WatchEntityPayloadCodec.ReadSingle(payload, 12);
            gate.icon.Rotation = WatchEntityPayloadCodec.ReadSingle(payload, 16);
        }
        return changed;
    }

    private static bool TryValidate(WatchEntityState state)
        => state.Key.Kind == WatchEntityKind.TouchSwitchAndSwitchGate
            && WatchPacketValidator.IsValid(state);

    private static byte EncodeAnimation(string animation)
        => animation switch { "idle" => 1, "spin" => 2, "active" => 3, _ => 0 };

    private static string? DecodeAnimation(byte animation)
        => animation switch { 1 => "idle", 2 => "spin", 3 => "active", _ => null };

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if (MiaoNetModule.IsWatching && StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            ApplyRemoteSwitchGates(self);
    }

    private static void SwitchGate_ctor(
        On.Celeste.SwitchGate.orig_ctor_EntityData_Vector2 orig,
        SwitchGate self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<SwitchGate>.Set(self, data.Level.Name, data.ID);
    }

    private static void TouchSwitch_ctor(
        On.Celeste.TouchSwitch.orig_ctor_EntityData_Vector2 orig,
        TouchSwitch self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<TouchSwitch>.Set(self, data.Level.Name, data.ID);
    }
}
