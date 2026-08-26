using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchBounceBlockAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 60;
    private const int BreakPayloadSize = 9;
    private const byte BreakEvent = 1;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte IceModeFlag = 1 << 2;
    private const byte ReformedFlag = 1 << 3;
    private const byte IceModeNextFlag = 1 << 4;
    private const byte KnownFlags = VisibleFlag
        | CollidableFlag
        | IceModeFlag
        | ReformedFlag
        | IceModeNextFlag;

    private readonly record struct BounceState(
        byte Flags,
        byte State,
        Vector2 Position,
        Vector2 BounceDirection,
        Vector2 DebrisDirection,
        Vector2 BounceLift,
        float MoveSpeed,
        float WindUpStartTimer,
        float WindUpProgress,
        float RespawnTimer,
        float BounceEndTimer,
        float ReappearFlash
    )
    {
        public bool Visible => (Flags & VisibleFlag) != 0;
        public bool Collidable => (Flags & CollidableFlag) != 0;
        public bool IceMode => (Flags & IceModeFlag) != 0;
        public bool Reformed => (Flags & ReformedFlag) != 0;
        public bool IceModeNext => (Flags & IceModeNextFlag) != 0;
    }

    private static readonly WatchBounceBlockAdapter instance = new();
    private static readonly Dictionary<int, BounceState> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.BounceBlock;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.BounceBlock.ctor_EntityData_Vector2 += BounceBlock_ctor;
        On.Celeste.BounceBlock.Break += BounceBlock_Break;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BounceBlock.Break -= BounceBlock_Break;
        On.Celeste.BounceBlock.ctor_EntityData_Vector2 -= BounceBlock_ctor;
        On.Celeste.Level.Update -= Level_Update;
        WatchEntityIDTable<BounceBlock>.Clear();
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (BounceBlock block in level.Entities.OfType<BounceBlock>())
        {
            if (WatchEntityIDTable<BounceBlock>.TryGet(block, room, out int id))
                yield return Encode(id, Capture(block));
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

        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || !TryDecode(state.Payload.Span, out BounceState desired))
                return WatchEntityApplyResult.None;
            if (!remoteStates.TryAdd(state.Key.EntityID, desired))
            {
                if (isCompleteState)
                    return WatchEntityApplyResult.None;
                remoteStates[state.Key.EntityID] = desired;
            }
        }

        bool changed = false;
        bool requiresReload = false;
        HashSet<int> found = new();
        foreach (BounceBlock block in level.Entities.OfType<BounceBlock>())
        {
            if (!WatchEntityIDTable<BounceBlock>.TryGet(block, room, out int id))
                continue;
            found.Add(id);
            if (remoteStates.TryGetValue(id, out BounceState desired))
                changed |= Apply(block, desired);
            else
                requiresReload |= isCompleteState;
        }
        if (remoteStates.Keys.Any(id => !found.Contains(id)))
            requiresReload = true;

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.Key.Kind != Kind
            || entityEvent.Key.SubID != 0
            || entityEvent.EventID != BreakEvent
            || entityEvent.Payload.Length != BreakPayloadSize)
            return;

        BounceBlock? block = level.Entities.OfType<BounceBlock>().FirstOrDefault(candidate =>
            WatchEntityIDTable<BounceBlock>.TryGet(candidate, level.Session.Level, out int id)
            && id == entityEvent.Key.EntityID
        );
        if (block is null)
            return;

        ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
        Vector2 debrisDirection = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 0),
            WatchEntityPayloadCodec.ReadSingle(payload, 4)
        );
        bool iceMode = payload[8] != 0;
        bool modeChanged = block.iceMode != iceMode;
        block.iceMode = iceMode;
        block.debrisDirection = debrisDirection;
        if (modeChanged)
            block.ToggleSprite();
        block.Break();
    }

    private static BounceState Capture(BounceBlock block)
    {
        byte flags = 0;
        if (block.Visible)
            flags |= VisibleFlag;
        if (block.Collidable)
            flags |= CollidableFlag;
        if (block.iceMode)
            flags |= IceModeFlag;
        if (block.reformed)
            flags |= ReformedFlag;
        if (block.iceModeNext)
            flags |= IceModeNextFlag;
        return new(
            flags,
            (byte)block.state,
            block.Position,
            block.bounceDir,
            block.debrisDirection,
            block.bounceLift,
            block.moveSpeed,
            block.windUpStartTimer,
            block.windUpProgress,
            block.respawnTimer,
            block.bounceEndTimer,
            block.reappearFlash
        );
    }

    private static bool Apply(BounceBlock block, BounceState desired)
    {
        BounceState current = Capture(block);
        if (current == desired)
            return false;

        Vector2 movement = desired.Position - block.Position;
        block.Position = desired.Position;
        if (movement != Vector2.Zero)
        {
            block.MoveStaticMovers(movement);
            block.ClearRemainder();
        }

        bool modeChanged = block.iceMode != desired.IceMode;
        bool collisionChanged = block.Collidable != desired.Collidable;
        block.Visible = desired.Visible;
        block.Collidable = desired.Collidable;
        block.iceMode = desired.IceMode;
        block.reformed = desired.Reformed;
        block.iceModeNext = desired.IceModeNext;
        block.state = (BounceBlock.States)desired.State;
        block.bounceDir = desired.BounceDirection;
        block.debrisDirection = desired.DebrisDirection;
        block.bounceLift = desired.BounceLift;
        block.moveSpeed = desired.MoveSpeed;
        block.windUpStartTimer = desired.WindUpStartTimer;
        block.windUpProgress = desired.WindUpProgress;
        block.respawnTimer = desired.RespawnTimer;
        block.bounceEndTimer = desired.BounceEndTimer;
        block.reappearFlash = desired.ReappearFlash;
        if (collisionChanged)
        {
            if (desired.Collidable)
                block.EnableStaticMovers();
            else
                block.DisableStaticMovers();
        }
        if (modeChanged)
            block.ToggleSprite();
        return true;
    }

    private static WatchEntityState Encode(int id, BounceState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        payload[1] = state.State;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.Position.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.BounceDirection.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.BounceDirection.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.DebrisDirection.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.DebrisDirection.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.BounceLift.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.BounceLift.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.MoveSpeed);
        WatchEntityPayloadCodec.WriteSingle(payload, 40, state.WindUpStartTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 44, state.WindUpProgress);
        WatchEntityPayloadCodec.WriteSingle(payload, 48, state.RespawnTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 52, state.BounceEndTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 56, state.ReappearFlash);
        return new(new WatchEntityKey(WatchEntityKind.BounceBlock, id), payload);
    }

    private static bool TryDecode(ReadOnlySpan<byte> payload, out BounceState state)
    {
        state = default;
        if (payload.Length != PayloadSize
            || (payload[0] & ~KnownFlags) != 0
            || payload[1] > (byte)BounceBlock.States.Broken
            || payload[2] != 0
            || payload[3] != 0)
            return false;

        float[] values = new float[14];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = WatchEntityPayloadCodec.ReadSingle(payload, 4 + index * 4);
            if (!float.IsFinite(values[index]))
                return false;
        }

        state = new(
            payload[0],
            payload[1],
            new(values[0], values[1]),
            new(values[2], values[3]),
            new(values[4], values[5]),
            new(values[6], values[7]),
            values[8],
            values[9],
            values[10],
            values[11],
            values[12],
            values[13]
        );
        return true;
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if (!MiaoNetModule.IsWatching || !StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            return;

        foreach (BounceBlock block in self.Entities.OfType<BounceBlock>())
        {
            if (WatchEntityIDTable<BounceBlock>.TryGet(block, self.Session.Level, out int id)
                && remoteStates.TryGetValue(id, out BounceState desired))
                Apply(block, desired);
        }
    }

    private static void BounceBlock_ctor(
        On.Celeste.BounceBlock.orig_ctor_EntityData_Vector2 orig,
        BounceBlock self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<BounceBlock>.Set(self, data.Level.Name, data.ID);
    }

    private static void BounceBlock_Break(On.Celeste.BounceBlock.orig_Break orig, BounceBlock self)
    {
        Level? level = self.Scene as Level;
        bool remoteControlled = level is not null
            && MiaoNetModule.IsWatching
            && WatchEntityIDTable<BounceBlock>.TryGet(self, level.Session.Level, out int remoteID)
            && remoteStates.ContainsKey(remoteID);
        if (remoteControlled && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;

        Vector2 debrisDirection = self.debrisDirection;
        bool iceMode = self.iceMode;
        int id = default;
        bool publish = level is not null
            && !WatchEntitySyncRegistry.IsApplyingRemoteState
            && WatchEntityIDTable<BounceBlock>.TryGet(self, level.Session.Level, out id);
        orig(self);
        if (!publish)
            return;

        byte[] payload = new byte[BreakPayloadSize];
        WatchEntityPayloadCodec.WriteSingle(payload, 0, debrisDirection.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 4, debrisDirection.Y);
        payload[8] = iceMode ? (byte)1 : (byte)0;
        WatchEntitySyncRegistry.PublishEvent(
            level!,
            new WatchEntityEvent(
                new WatchEntityKey(WatchEntityKind.BounceBlock, id),
                BreakEvent,
                payload
            )
        );
    }
}
