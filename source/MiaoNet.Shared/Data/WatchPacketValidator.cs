using System.Text;
using System.Buffers.Binary;

namespace MiaoNet.Shared;

public static class WatchPacketValidator
{
    private const int MaxCassetteBlockHeight = 64;
    public const int MaxFlagCount = 4096;
    public const int MaxFlagUtf8Bytes = 1024;
    public const int MaxTouchSwitchCount = 4096;
    public const int MaxEntityStateCount = 4096;
    public const int MaxEntityEventCount = 4096;
    public const int MaxEntityPayloadBytes = 1024;

    private const int MaxSnapshotFlagsSize = 30_000;

    public static bool IsValid(WatchSceneSnapshot snapshot)
    {
        if (!snapshot.Location.IsInMap
            || snapshot.Sequence < 0
            || !TryGetFlagsSerializedSize(snapshot.Flags, out int flagsSize)
            || flagsSize > MaxSnapshotFlagsSize
            || !TryGetTouchSwitchesSerializedSize(snapshot.ActiveTouchSwitchIDs, out int switchesSize)
            || !TryGetEntityStatesSerializedSize(snapshot.EntityStates, out int entityStatesSize))
            return false;

        // A successful snapshot is first returned in PacketWatchSnapshotResponse,
        // then forwarded in PacketWatchStartResponse or PacketWatchResyncSnapshot.
        // The start response is the larger envelope, so validating against it also
        // keeps the resync notification within the payload limit.
        long packetSize = sizeof(int) * 2L + sizeof(byte)
            + GetLocationSerializedSize(snapshot.Location)
            + sizeof(int) + flagsSize + switchesSize + entityStatesSize;
        return packetSize <= Connection.MaxPayloadSize;
    }
    public static bool IsValid(WatchEntityState state)
        => IsValidEntityKey(state.Key)
            && state.Payload.Length <= MaxEntityPayloadBytes
            && IsValidEntityStatePayload(state);

    public static bool IsValid(WatchEntityEvent entityEvent)
        => IsValidEntityKey(entityEvent.Key)
            && entityEvent.EventID != 0
            && entityEvent.Payload.Length <= MaxEntityPayloadBytes
            && IsValidEntityEventPayload(entityEvent);


    public static bool IsValid(WatchSceneDelta delta)
    {
        if (!delta.Location.IsInMap
            || delta.Sequence <= 0
            || (delta.AddedFlags.Count == 0
                && delta.RemovedFlags.Count == 0
                && !delta.RequiresRoomReload
                && !delta.HasTouchSwitchState
                && delta.EntityStateMode == WatchEntityStateMode.None
                && delta.EntityEvents.Count == 0)
            || (long)delta.AddedFlags.Count + delta.RemovedFlags.Count > MaxFlagCount
            || !TryGetFlagsSerializedSize(delta.AddedFlags, out int addedSize)
            || !TryGetFlagsSerializedSize(delta.RemovedFlags, out int removedSize)
            || (delta.HasTouchSwitchState
                && !TryGetTouchSwitchesSerializedSize(delta.ActiveTouchSwitchIDs, out _))
            || (delta.RequiresRoomReload && !delta.HasTouchSwitchState)
            || (delta.IsDeathRespawn
                && (delta.RequiresRoomReload
                    || !delta.HasTouchSwitchState
                    || delta.EntityStateMode != WatchEntityStateMode.Replace
                    || delta.RoomTransition.HasValue))
            || (delta.RoomTransition.HasValue
                && (!delta.HasTouchSwitchState
                    || delta.EntityStateMode != WatchEntityStateMode.Replace
                    || delta.RequiresRoomReload
                    || !IsValidRoomTransition(delta.Location, delta.RoomTransition.Value)))
            || (!delta.HasTouchSwitchState && delta.ActiveTouchSwitchIDs.Count != 0)
            || !TryGetEntityStatesSerializedSize(delta.EntityStates, out int entityStatesSize)
            || !TryGetEntityEventsSerializedSize(delta.EntityEvents, out int entityEventsSize)
            || !IsValidEntityStateMode(delta)
            || (delta.RequiresRoomReload && delta.EntityStateMode != WatchEntityStateMode.Replace))
            return false;

        HashSet<string> added = new(delta.AddedFlags, StringComparer.Ordinal);
        if (delta.RemovedFlags.Any(added.Contains))
            return false;

        int switchesSize = delta.HasTouchSwitchState
            ? sizeof(ushort) + sizeof(int) * delta.ActiveTouchSwitchIDs.Count
            : 0;
        int roomTransitionSize = delta.RoomTransition.HasValue
            ? GetLocationSerializedSize(delta.RoomTransition.Value.SourceLocation)
                + GetLocationSerializedSize(delta.RoomTransition.Value.TargetLocation)
                + sizeof(float) * 4
            : 0;
        long notificationSize = sizeof(int) * 3L
            + GetLocationSerializedSize(delta.Location)
            + addedSize + removedSize
            + sizeof(bool) * 4L + roomTransitionSize + switchesSize
            + sizeof(byte) + entityStatesSize + entityEventsSize;
        return notificationSize <= Connection.MaxPayloadSize;
    }

    private static bool IsValidRoomTransition(
        PlayerLocation targetLocation,
        WatchRoomTransition transition
    )
    {
        Vector2 direction = transition.Direction;
        Vector2 position = transition.PlayerPosition;
        bool cardinalDirection = (direction.X is 1f or -1f && direction.Y == 0f)
            || (direction.Y is 1f or -1f && direction.X == 0f);
        return transition.SourceLocation.IsInMap
            && transition.TargetLocation == targetLocation
            && transition.SourceLocation.Map == targetLocation.Map
            && transition.SourceLocation.Room != targetLocation.Room
            && cardinalDirection
            && float.IsFinite(position.X)
            && float.IsFinite(position.Y);
    }

    private static bool IsValidEntityStateMode(WatchSceneDelta delta)
        => delta.EntityStateMode switch
        {
            WatchEntityStateMode.None => delta.EntityStates.Count == 0,
            WatchEntityStateMode.Patch => delta.EntityStates.Count > 0,
            WatchEntityStateMode.Replace => true,
            _ => false,
        };

    private static bool TryGetEntityStatesSerializedSize(
        IReadOnlyCollection<WatchEntityState> states,
        out int serializedSize
    )
    {
        serializedSize = sizeof(ushort);
        if (states.Count > MaxEntityStateCount)
            return false;

        HashSet<WatchEntityKey> keys = new();
        foreach (WatchEntityState state in states)
        {
            if (!IsValid(state)
                || !keys.Add(state.Key))
                return false;

            serializedSize += GetEntityKeySerializedSize()
                + sizeof(ushort) + state.Payload.Length;
        }
        return true;
    }

    private static bool TryGetEntityEventsSerializedSize(
        IReadOnlyCollection<WatchEntityEvent> events,
        out int serializedSize
    )
    {
        serializedSize = sizeof(ushort);
        if (events.Count > MaxEntityEventCount)
            return false;

        foreach (WatchEntityEvent entityEvent in events)
        {
            if (!IsValid(entityEvent))
                return false;

            serializedSize += GetEntityKeySerializedSize()
                + sizeof(byte) + sizeof(ushort) + entityEvent.Payload.Length;
        }
        return true;
    }

    private static bool IsValidEntityKey(WatchEntityKey key)
        => key.EntityID >= 0
            && key.Kind != WatchEntityKind.None
            && Enum.IsDefined(typeof(WatchEntityKind), key.Kind);

    private static bool HasStatePayload(WatchEntityState state, int length, ushort subID = 0)
        => state.Key.SubID == subID && state.Payload.Length == length;

    private static bool HasEventPayload(
        WatchEntityEvent entityEvent,
        byte eventID,
        int length,
        ushort subID = 0
    ) => entityEvent.Key.SubID == subID
        && entityEvent.EventID == eventID
        && entityEvent.Payload.Length == length;

    private static bool IsValidEntityStatePayload(WatchEntityState state)
        => state.Key.Kind switch
        {
            WatchEntityKind.PersistentSession => state.Key.EntityID == 0
                && state.Key.SubID == 0
                && WatchPersistentSceneState.TryFromPayload(state.Payload.Span, out _),
            WatchEntityKind.Checkpoint or WatchEntityKind.SummitCheckpoint =>
                HasStatePayload(state, 1)
                && state.Payload.Span[0] <= 1,
            WatchEntityKind.WingedStrawberry => HasStatePayload(state, 1)
                && state.Payload.Span[0] <= (byte)WatchWingedStrawberryState.Absent,
            WatchEntityKind.Spring => HasStatePayload(state, 1)
                && state.Payload.Span[0] <= 1,
            WatchEntityKind.Refill or WatchEntityKind.FlyFeather =>
                HasStatePayload(state, 1)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Gone,
            WatchEntityKind.Booster => HasStatePayload(state, 16)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Returning
                && state.Payload.Span[9] <= 1
                && state.Payload.Span[10] <= 1
                && state.Payload.Span[11] <= 1
                && HasFiniteSingles(state.Payload.Span, 1, 5, 12),
            WatchEntityKind.Bumper => HasStatePayload(state, 2)
                && state.Payload.Span[0] <= 1
                && state.Payload.Span[1] <= 1,
            WatchEntityKind.Cloud => HasStatePayload(state, 10)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Returning
                && state.Payload.Span[9] <= 1
                && HasFiniteSingles(state.Payload.Span, 1, 5),
            WatchEntityKind.DashSwitch => HasStatePayload(state, 9)
                && state.Payload.Span[0] <= 1
                && HasFiniteSingles(state.Payload.Span, 1, 5),
            WatchEntityKind.TempleGate => HasStatePayload(state, 6)
                && state.Payload.Span[0] <= 1
                && state.Payload.Span[5] <= 1
                && HasFiniteSingles(state.Payload.Span, 1),
            WatchEntityKind.CrumblePlatform => state.Key.SubID == 0
                && IsValidCrumblePlatformPayload(state.Payload.Span),
            WatchEntityKind.CoreMode => state.Key.EntityID == 0
                && HasStatePayload(state, 1)
                && state.Payload.Span[0] <= 2,
            WatchEntityKind.HeartGemDoor => HasStatePayload(state, 24)
                && state.Payload.Span[0] <= 1
                && state.Payload.Span[17] <= 1
                && state.Payload.Span[18] <= 1
                && state.Payload.Span[19] <= 1
                && HasFiniteSingles(state.Payload.Span, 1, 5, 9, 13, 20),
            WatchEntityKind.FakeHeart => HasStatePayload(state, 1)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Cooldown,
            WatchEntityKind.MovingSolid => state.Key.SubID == 0
                && IsValidMovingSolidPayload(state.Payload.Span),
            WatchEntityKind.DashBlock => HasStatePayload(state, 1)
                && state.Payload.Span[0] <= 1,
            WatchEntityKind.StrawberrySeed => IsValidStrawberrySeedPayload(state),
            WatchEntityKind.BounceBlock => state.Key.SubID == 0
                && IsValidBounceBlockPayload(state.Payload.Span),
            WatchEntityKind.PeriodicPlatform => state.Key.SubID == 0
                && IsValidPeriodicPlatformPayload(state.Payload.Span),
            WatchEntityKind.CassetteBlock => IsValidCassetteBlockPayload(state),
            WatchEntityKind.SwitchGate => state.Key.SubID == 0
                && IsValidSwitchGatePayload(state.Payload.Span),
            WatchEntityKind.ClutterSystem => IsValidClutterSystemPayload(state),
            WatchEntityKind.DoorMechanism => IsValidDoorMechanismPayload(state),
            WatchEntityKind.Key => HasStatePayload(state, 12)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Returning
                && (state.Payload.Span[1] & ~0b0000_0111) == 0
                && state.Payload.Span[2] == 0 && state.Payload.Span[3] == 0
                && HasFiniteSingles(state.Payload.Span, 4, 8),
            WatchEntityKind.LockBlock => HasStatePayload(state, 4)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Gone
                && (state.Payload.Span[1] & ~0b0000_0111) == 0
                && state.Payload.Span[2] == 0 && state.Payload.Span[3] == 0,
            WatchEntityKind.TheoCrystal or WatchEntityKind.Glider => state.Key.SubID == 0
                && IsValidHoldableEntityPayload(state.Payload.Span),
            WatchEntityKind.TheoCrystalPedestal => HasStatePayload(state, 1)
                && state.Payload.Span[0] <= 1,
            WatchEntityKind.BadelineBoost => HasStatePayload(state, 16)
                && state.Payload.Span[0] <= (byte)WatchEntityPhase.Returning
                && (state.Payload.Span[1] & ~0b0001_1111) == 0
                && HasFiniteSingles(state.Payload.Span, 4, 8, 12)
                && BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
                    state.Payload.Span[12..]
                )) is >= 0f and <= 1f,
            WatchEntityKind.FlingBird => HasStatePayload(state, 56)
                && state.Payload.Span[0] <= 4
                && (state.Payload.Span[1] & ~0b0000_0111) == 0
                && (state.Payload.Span[4] <= 3 || state.Payload.Span[4] == byte.MaxValue)
                && state.Payload.Span[6] == 0 && state.Payload.Span[7] == 0
                && HasFiniteSingles(
                    state.Payload.Span,
                    8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52
                )
                && ReadSingle(state.Payload.Span, 32) is >= 0f and <= 10000f,
            WatchEntityKind.WallBooster => HasStatePayload(state, 2)
                && state.Payload.Span[0] <= 1
                && state.Payload.Span[1] <= 1,
            WatchEntityKind.Torch or WatchEntityKind.TempleCrackedBlock =>
                HasStatePayload(state, 1)
                && state.Payload.Span[0] <= 1,
            WatchEntityKind.TempleBigEyeball => HasStatePayload(state, 2)
                && state.Payload.Span[0] <= 1
                && state.Payload.Span[1] <= 1,
            WatchEntityKind.StaticSpinner => HasStatePayload(state, 1)
                && state.Payload.Span[0] == 1,
            WatchEntityKind.TriggerSpikes => IsValidTriggerSpikesPayload(state),
            WatchEntityKind.FireBall => IsValidFireBallPayload(state),
            WatchEntityKind.Lava => IsValidLavaPayload(state),
            WatchEntityKind.BadelineOldsite => IsValidBadelineOldsitePayload(state),
            WatchEntityKind.Snowball => IsValidSnowballPayload(state),
            WatchEntityKind.Puffer => IsValidPufferPayload(state),
            WatchEntityKind.AngryOshiro => IsValidAngryOshiroPayload(state),
            WatchEntityKind.SeekerSystem => IsValidSeekerSystemPayload(state),
            WatchEntityKind.SeekerBarrier => IsValidSeekerBarrierPayload(state),
            WatchEntityKind.PlayerSeeker => IsValidPlayerSeekerPayload(state),
            WatchEntityKind.FinalBoss => IsValidFinalBossPayload(state),
            WatchEntityKind.FinalBossShot => IsValidFinalBossShotPayload(state),
            WatchEntityKind.FinalBossBeam => IsValidFinalBossBeamPayload(state),
            WatchEntityKind.FinalBossMovingBlock => IsValidFinalBossMovingBlockPayload(state),
            WatchEntityKind.ReflectionTentacles => IsValidReflectionTentaclesPayload(state),
            WatchEntityKind.LightningBreakerBox => IsValidLightningBreakerBoxPayload(state),
            WatchEntityKind.Lightning => IsValidLightningPayload(state),
            WatchEntityKind.BirdPath => IsValidBirdPathPayload(state),
            WatchEntityKind.WhiteBlock => IsValidWhiteBlockPayload(state),
            WatchEntityKind.ForsakenCitySatellite => IsValidSatellitePayload(state),
            WatchEntityKind.ReflectionHeartStatue => IsValidReflectionHeartStatuePayload(state),
            WatchEntityKind.RidgeGate => IsValidRidgeGatePayload(state),
            WatchEntityKind.RoomEnvironment => IsValidRoomEnvironmentPayload(state),
            WatchEntityKind.RumbleTrigger => IsValidRumbleTriggerPayload(state),
            WatchEntityKind.RumbleWall => HasStatePayload(state, 0),
            WatchEntityKind.Bridge => IsValidBridgePayload(state),
            WatchEntityKind.IntroCrusher => IsValidFixedStatePayload(state, 28, 0b0000_0111, 4, 8, 12, 16, 20, 24)
                && HasZeroBytes(state.Payload.Span, 1, 2, 3),
            WatchEntityKind.ResortRoofEnding => IsValidResortRoofPayload(state),
            WatchEntityKind.BirdNPC => IsValidFixedStatePayload(state, 44, 0b0000_0111, 8, 12, 16, 20, 24, 28, 32, 36, 40)
                && state.Payload.Span[2] <= 1 && IsValidOptionalIndex(state.Payload.Span[3], 6)
                && HasZeroBytes(state.Payload.Span, 5, 6, 7)
                && ReadSingle(state.Payload.Span, 28) is >= 0f and <= 1f,
            WatchEntityKind.FlutterBird => IsValidFixedStatePayload(state, 28, 0b0000_0011, 4, 8, 12, 16, 20, 24)
                && IsValidOptionalIndex(state.Payload.Span[1], 6) && state.Payload.Span[3] == 0,
            WatchEntityKind.MoonCreature => IsValidFixedStatePayload(state, 48, 0b0000_0011, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44)
                && IsValidOptionalIndex(state.Payload.Span[2], 6),
            WatchEntityKind.FlingBirdIntro => IsValidFixedStatePayload(state, 36, 0b0001_1111, 4, 8, 12, 16, 20, 24, 28, 32)
                && IsValidOptionalIndex(state.Payload.Span[1], 6) && state.Payload.Span[3] == 0,
            WatchEntityKind.DreamMirror => IsValidFixedStatePayload(state, 24, 0b0111_1111, 4, 8, 12, 16, 20)
                && IsValidOptionalIndex(state.Payload.Span[1], 13) && state.Payload.Span[3] == 0,
            WatchEntityKind.ResortMirror => IsValidFixedStatePayload(state, 24, 0b0001_1111, 4, 8, 12, 16, 20)
                && IsValidOptionalIndex(state.Payload.Span[1], 13) && state.Payload.Span[3] == 0,
            WatchEntityKind.TempleMirrorPortal => IsValidFixedStatePayload(state, 28, 0b0001_1111, 8, 12, 16, 20, 24)
                && IsValidOptionalIndex(state.Payload.Span[1], 14)
                && (state.Payload.Span[3] & ~0b0000_1111) == 0
                && BinaryPrimitives.ReadInt32LittleEndian(state.Payload.Span[4..]) is >= 0 and <= 1024,
            WatchEntityKind.Gondola => IsValidFixedStatePayload(state, 40, 0b0011_1111, 8, 12, 16, 20, 24, 28, 32, 36)
                && IsValidOptionalIndex(state.Payload.Span[1], 13)
                && IsValidOptionalIndex(state.Payload.Span[3], 13)
                && HasZeroBytes(state.Payload.Span, 5, 6, 7),
            WatchEntityKind.WaveDashTutorial => IsValidFixedStatePayload(state, 36, 0b0001_1111, 8, 12, 16, 20, 24, 32)
                && IsValidOptionalIndex(state.Payload.Span[1], 13)
                && IsValidOptionalIndex(state.Payload.Span[3], 13)
                && HasZeroBytes(state.Payload.Span, 5, 6, 7)
                && BinaryPrimitives.ReadInt32LittleEndian(state.Payload.Span[28..]) is >= 0 and <= 64,
            WatchEntityKind.PowerSourceNumber => IsValidFixedStatePayload(state, 20, 0b0000_1111, 4, 8, 12, 16)
                && HasZeroBytes(state.Payload.Span, 1, 2, 3),
            WatchEntityKind.NarrativeNPC => IsValidFixedStatePayload(state, 36, 0b0011_1111, 8, 12, 16, 20, 24, 32)
                && state.Payload.Span[4] <= (byte)WatchNarrativeNPCVisual.BadelineBoss
                && HasZeroBytes(state.Payload.Span, 5, 6, 7)
                && ReadSingle(state.Payload.Span, 24) is >= 0f and <= 1f,
            WatchEntityKind.AscendManager => IsValidAscendManagerPayload(state),
            WatchEntityKind.IntroCar => IsValidFixedStatePayload(state, 20, 0b0000_0111, 4, 8, 12)
                && HasZeroBytes(state.Payload.Span, 1, 2, 3),
            WatchEntityKind.ChapterProp => state.Key.SubID is 1 or 2
                && state.Payload.Length == 28
                && (state.Payload.Span[0] & ~0b0000_0111) == 0
                && HasFiniteSingles(state.Payload.Span, 4, 8, 12, 16, 20)
                && ReadSingle(state.Payload.Span, 12) is >= 0f and <= 1f
                && ReadSingle(state.Payload.Span, 16) is >= 0f and <= 1f
                && HasZeroBytes(state.Payload.Span, 26, 27),
            WatchEntityKind.Lookout => IsValidFixedStatePayload(state, 20, 0b0000_0011, 4, 8, 16)
                && BinaryPrimitives.ReadInt32LittleEndian(state.Payload.Span[12..]) >= 0,
            WatchEntityKind.ConditionalBlock => state.Key.SubID is 1 or 2
                && state.Payload.Length == 16
                && (state.Payload.Span[0] & ~0b0000_0111) == 0
                && HasFiniteSingles(state.Payload.Span, 4, 8, 12)
                && state.Payload.Span[1] == state.Key.SubID - 1
                && state.Payload.Span[2] <= 1
                && state.Payload.Span[3] == 0
                && ReadSingle(state.Payload.Span, 4) is >= 0f and <= 1f
                && ReadSingle(state.Payload.Span, 8) is >= 0f and <= 1f
                && ReadSingle(state.Payload.Span, 12) is >= 0f and <= 1f,
            WatchEntityKind.BadelineDummy => IsValidFixedStatePayload(
                    state, 40, 0b0001_1111, 8, 12, 16, 20, 24, 28, 32, 36)
                && ReadSingle(state.Payload.Span, 32) is >= 0f and <= 1f
                && ReadSingle(state.Payload.Span, 36) is >= 0f and <= 1f,
            _ => false,
        };

    private static bool IsValidEntityEventPayload(WatchEntityEvent entityEvent)
        => entityEvent.Key.Kind switch
        {
            WatchEntityKind.Spring => HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.Bumper => HasEventPayload(entityEvent, 1, 16)
                && HasFiniteSingles(entityEvent.Payload.Span, 0, 4, 8, 12),
            WatchEntityKind.FlyFeather => entityEvent.Key.SubID == 0
                && (entityEvent.EventID switch
                {
                    1 => entityEvent.Payload.Length == 9
                        && entityEvent.Payload.Span[8] <= 1
                        && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
                    2 => entityEvent.Payload.Length == 8
                        && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
                    3 => entityEvent.Payload.Length == 0,
                    _ => false,
                }),
            WatchEntityKind.FakeHeart => entityEvent.Key.SubID == 0
                && (entityEvent.EventID switch
                {
                    1 => entityEvent.Payload.Length == 4
                        && HasFiniteSingles(entityEvent.Payload.Span, 0),
                    2 => entityEvent.Payload.Length == 8
                        && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
                    3 => entityEvent.Payload.Length == 0,
                    _ => false,
                }),
            WatchEntityKind.CrumblePlatform => entityEvent.Key.SubID == 0
                && (entityEvent.EventID switch
                {
                    1 or 2 => entityEvent.Payload.Length == 6
                        && HasFiniteSingles(entityEvent.Payload.Span, 2),
                    3 => entityEvent.Payload.Length == 4
                        && HasFiniteSingles(entityEvent.Payload.Span, 0),
                    _ => false,
                }),
            WatchEntityKind.DashBlock => HasEventPayload(entityEvent, 1, 18)
                && entityEvent.Payload.Span[16] <= 1
                && entityEvent.Payload.Span[17] <= 1
                && HasFiniteSingles(entityEvent.Payload.Span, 0, 4, 8, 12),
            WatchEntityKind.BounceBlock => HasEventPayload(entityEvent, 1, 9)
                && entityEvent.Payload.Span[8] <= 1
                && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
            WatchEntityKind.ClutterSystem => entityEvent.Key.SubID == 3
                && entityEvent.Key.EntityID is >= 0 and <= 2
                && entityEvent.EventID == 1
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.DoorMechanism => entityEvent.Key.SubID <= 2
                && entityEvent.EventID == entityEvent.Key.SubID + 1
                && entityEvent.EventID switch
                {
                    1 => entityEvent.Payload.Length == 4
                        && HasFiniteSingles(entityEvent.Payload.Span, 0),
                    2 => entityEvent.Payload.Length == 1
                        && entityEvent.Payload.Span[0] <= 1,
                    3 => entityEvent.Payload.Length == 0,
                      _ => false,
                  },
            WatchEntityKind.Key => entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 3
                && (entityEvent.EventID switch
                {
                    1 or 3 => entityEvent.Payload.Length == 0,
                    2 => entityEvent.Payload.Length == 12
                        && HasFiniteSingles(entityEvent.Payload.Span, 4, 8),
                    _ => false,
                }),
            WatchEntityKind.LockBlock => HasEventPayload(entityEvent, 1, 4),
            WatchEntityKind.TheoCrystal or WatchEntityKind.Glider =>
                entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 3
                && (entityEvent.EventID switch
                {
                    1 => entityEvent.Payload.Length == 0,
                    2 => entityEvent.Payload.Length == 16
                        && HasFiniteSingles(entityEvent.Payload.Span, 0, 4, 8, 12),
                    3 => entityEvent.Payload.Length == 0,
                    _ => false,
                }),
            WatchEntityKind.BadelineBoost or WatchEntityKind.FlingBird =>
                HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.Torch => HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.TempleCrackedBlock => HasEventPayload(entityEvent, 1, 8)
                && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
            WatchEntityKind.TempleBigEyeball => entityEvent.Key.SubID == 0
                && entityEvent.EventID is 1 or 2
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.StaticSpinner => HasEventPayload(entityEvent, 1, 1)
                && entityEvent.Payload.Span[0] <= 1,
            WatchEntityKind.FireBall => entityEvent.EventID == 1
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.TriggerSpikes => entityEvent.EventID == 1
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.Snowball => entityEvent.Key.SubID == 0
                && (entityEvent.EventID switch
                {
                    1 => entityEvent.Payload.Length == 0,
                    2 => entityEvent.Payload.Length == 1
                        && entityEvent.Payload.Span[0] <= 1,
                    _ => false,
                }),
            WatchEntityKind.Puffer => HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.AngryOshiro => entityEvent.Key.EntityID == 0
                && HasEventPayload(entityEvent, 1, 8)
                && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
            WatchEntityKind.SeekerSystem => entityEvent.Key.SubID == 0
                && entityEvent.EventID switch
                {
                    1 or 2 or 4 or 5 or 6 => entityEvent.Payload.Length == 0,
                    3 => entityEvent.Payload.Length == 17
                        && entityEvent.Payload.Span[0] <= 3
                        && HasFiniteSingles(entityEvent.Payload.Span, 1, 5, 9, 13),
                    _ => false,
                },
            WatchEntityKind.SeekerBarrier => HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.PlayerSeeker => entityEvent.Key.SubID == 0
                && entityEvent.EventID switch
                {
                    1 => entityEvent.Payload.Length == 0,
                    2 => entityEvent.Payload.Length == 8
                        && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
                    3 => entityEvent.Payload.Length == 2
                        && entityEvent.Payload.Span[0] <= 3
                        && entityEvent.Payload.Span[1] <= 2,
                    _ => false,
                },
            WatchEntityKind.FinalBoss => entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 2
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.FinalBossBeam => entityEvent.Key.SubID != 0
                && entityEvent.EventID == 1
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.FinalBossMovingBlock => entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 2
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.ReflectionTentacles => entityEvent.Key.SubID <= 3
                && entityEvent.EventID is >= 1 and <= 2
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.LightningBreakerBox => entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 2
                && entityEvent.Payload.Length == 8
                && HasFiniteSingles(entityEvent.Payload.Span, 0, 4),
            WatchEntityKind.Lightning => HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.BirdPath => entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 2
                && entityEvent.Payload.Length == 0,
            WatchEntityKind.WhiteBlock or WatchEntityKind.RidgeGate =>
                HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.ForsakenCitySatellite => entityEvent.EventID switch
            {
                1 => entityEvent.Key.SubID == 0 && entityEvent.Payload.Length == 0,
                2 => entityEvent.Key.SubID is >= 1 and <= 5
                    && entityEvent.Payload.Length == 0,
                3 => entityEvent.Key.SubID is >= 1 and <= 5
                    && entityEvent.Payload.Length == 4
                    && HasFiniteSingles(entityEvent.Payload.Span, 0)
                    && ReadSingle(entityEvent.Payload.Span, 0) is >= 0f and <= 3f,
                _ => false,
            },
            WatchEntityKind.ReflectionHeartStatue => entityEvent.Payload.Length == 0
                && (entityEvent.EventID switch
                {
                    1 => entityEvent.Key.SubID is >= 1 and <= 4,
                    2 => entityEvent.Key.SubID == 0,
                    _ => false,
                }),
            WatchEntityKind.RumbleTrigger => HasEventPayload(entityEvent, 1, 4)
                && HasFiniteSingles(entityEvent.Payload.Span, 0),
            WatchEntityKind.RumbleWall => HasEventPayload(entityEvent, 1, 0),
            WatchEntityKind.Bridge => entityEvent.Key.SubID != 0
                && entityEvent.EventID == 1
                && entityEvent.Payload.Length == 4
                && HasFiniteSingles(entityEvent.Payload.Span, 0)
                && ReadSingle(entityEvent.Payload.Span, 0) is >= 0f and <= 1f,
            WatchEntityKind.MovingSolid => entityEvent.Key.SubID == 0
                && entityEvent.EventID is >= 1 and <= 3
                && entityEvent.Payload.Length == 0,
            _ => false,
        };

    private static bool IsValidRoomEnvironmentPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.EntityID != 0 || state.Key.SubID != 0
            || payload.Length < 72 || payload.Length > 328
            || (payload[0] & ~0b0000_0111) != 0
            || payload[1] > 3 || payload[2] > 14 || payload[3] != 0
            || !HasFiniteSingles(payload, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60))
            return false;
        int length = BinaryPrimitives.ReadUInt16LittleEndian(payload[64..]);
        int colorGradeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[66..]);
        if (length > 192 || colorGradeLength > 64
            || payload.Length != 72 + length + colorGradeLength)
            return false;
        try
        {
            UTF8Encoding utf8 = new(false, true);
            _ = utf8.GetString(payload.Slice(72, length));
            _ = utf8.GetString(payload.Slice(72 + length, colorGradeLength));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsValidRumbleTriggerPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 16)
            && (payload[0] & ~0b0000_0111) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12);
    }

    private static bool IsValidBridgePayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID == 0)
            return payload.Length == 16 && (payload[0] & ~0b0000_1111) == 0
                && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
                && HasFiniteSingles(payload, 4, 8, 12);
        return payload.Length == 32 && (payload[0] & ~0b0000_0001) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28)
            && ReadSingle(payload, 16) is >= 0f and <= 1f
            && ReadSingle(payload, 28) is >= -0.1f and <= 1f;
    }

    private static bool IsValidResortRoofPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID == 0)
            return payload.Length == 1 && payload[0] <= 1;
        return payload.Length == 28 && (payload[0] & ~0b0000_0001) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24)
            && ReadSingle(payload, 24) is >= 0f and <= 1f;
    }

    private static bool IsValidFixedStatePayload(
        WatchEntityState state,
        int length,
        byte allowedFlags,
        params ReadOnlySpan<int> floatOffsets
    )
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, length)
            && (payload[0] & ~allowedFlags) == 0
            && HasFiniteSingles(payload, floatOffsets);
    }

    private static bool IsValidAscendManagerPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID == 0)
            return payload.Length == 20
                && (payload[0] & ~0b0000_1111) == 0
                && HasZeroBytes(payload, 1, 2, 3)
                && HasFiniteSingles(payload, 8, 12)
                && ReadSingle(payload, 8) is >= 0f and <= 1f;
        return state.Key.SubID == 1
            && payload.Length == 20
            && (payload[0] & ~0b0000_0001) == 0
            && HasZeroBytes(payload, 1, 2, 3)
            && BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) == state.Key.EntityID
            && HasFiniteSingles(payload, 8, 12, 16)
            && ReadSingle(payload, 8) is >= 0f and <= 1f;
    }

    private static bool IsValidOptionalIndex(byte value, byte maximum)
        => value <= maximum || value == byte.MaxValue;

    private static bool HasZeroBytes(ReadOnlySpan<byte> payload, params ReadOnlySpan<int> offsets)
    {
        foreach (int offset in offsets)
            if (offset < 0 || offset >= payload.Length || payload[offset] != 0)
                return false;
        return true;
    }

    private static bool IsValidLightningBreakerBoxPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 24)
            && (payload[0] & ~0b0000_0011) == 0
            && payload[1] <= 2
            && (payload[2] <= 3 || payload[2] == byte.MaxValue)
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20);
    }

    private static bool IsValidLightningPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 24)
            && (payload[0] & ~0b0000_1111) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20)
            && ReadSingle(payload, 12) is >= 0f and <= 1f
            && ReadSingle(payload, 16) is >= 0f and <= 0.6f
            && ReadSingle(payload, 20) is >= 0f and <= 1f
            && ((payload[0] & 0b0000_0100) != 0
                || ((payload[0] & 0b0000_1000) == 0 && ReadSingle(payload, 20) == 0f));
    }

    private static bool IsValidBirdPathPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 32)
            && (payload[0] & ~0b0000_0011) == 0
            && (payload[1] <= 4 || payload[1] == byte.MaxValue)
            && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28);
    }

    private static bool IsValidWhiteBlockPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 12)
            && (payload[0] & ~0b0001_1111) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4)
            && ReadSingle(payload, 4) is >= 0f and <= 10f;
    }

    private static bool IsValidSatellitePayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID == 0)
            return payload.Length == 32
                && payload[1] <= 6
                && HasValidDirections(payload, 2, payload[1], 6)
                && HasFiniteSingles(payload, 8, 12, 24, 28)
                && ReadSingle(payload, 24) is >= 0f and <= 2f
                && ReadSingle(payload, 28) is >= 0f and <= 2f;
        return state.Key.SubID <= 5
            && payload.Length == 48
            && (payload[0] & ~0b0001_1111) == 0
            && (payload[1] <= 5 || payload[1] == byte.MaxValue)
            && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40);
    }

    private static bool IsValidReflectionHeartStatuePayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 12)
            && (payload[0] & ~0b0000_0111) == 0
            && (payload[1] & ~0b0000_1111) == 0
            && payload[2] <= 6
            && payload[3] == 0 && payload[10] == 0 && payload[11] == 0
            && HasValidDirections(payload, 4, payload[2], 6);
    }

    private static bool IsValidRidgeGatePayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 24)
            && (payload[0] & ~0b0000_0111) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16);
    }

    private static bool HasValidDirections(
        ReadOnlySpan<byte> payload,
        int offset,
        int count,
        int capacity
    )
    {
        for (int index = 0; index < capacity; index++)
        {
            byte value = payload[offset + index];
            if (index < count ? value is 0 or > 5 : value != 0)
                return false;
        }
        return true;
    }

    private static float ReadSingle(ReadOnlySpan<byte> payload, int offset)
        => BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[offset..])
        );

    private static bool IsValidFinalBossPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 36)
            && (payload[0] & ~0b0001_1111) == 0
            && (payload[1] <= (byte)WatchFinalBossAnimation.LookingUp
                || payload[1] == (byte)WatchFinalBossAnimation.Unknown)
            && payload[3] <= 1
            && BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) is >= 0 and <= 1024
            && BinaryPrimitives.ReadInt32LittleEndian(payload[8..]) is >= 0 and <= 32
            && HasFiniteSingles(payload, 16, 20, 24, 28, 32);
    }

    private static bool IsValidFinalBossShotPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.SubID != 0
            && payload.Length == 56
            && (payload[0] & ~0b0000_0111) == 0
            && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52);
    }

    private static bool IsValidFinalBossBeamPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.SubID != 0
            && payload.Length == 28
            && payload[0] <= (byte)WatchFinalBossBeamPhase.Dissipating
            && payload[1] <= 2
            && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24);
    }

    private static bool IsValidFinalBossMovingBlockPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 36)
            && (payload[0] & ~0b0000_1111) == 0
            && payload[2] == 0 && payload[3] == 0
            && BinaryPrimitives.ReadInt32LittleEndian(payload[4..]) is >= 0 and <= 1024
            && BinaryPrimitives.ReadInt32LittleEndian(payload[8..]) is >= 0 and <= 1024
            && HasFiniteSingles(payload, 12, 16, 20, 24, 28, 32)
            && ReadSingle(payload, 32) is >= 0f and <= 1f;
    }

    private static bool IsValidReflectionTentaclesPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID > 3
            || payload.Length != 52
            || (payload[0] & ~0b0000_0001) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0
            || !HasFiniteSingles(payload, 16, 20, 24, 28, 32, 36, 40, 44, 48))
            return false;

        int index = BinaryPrimitives.ReadInt32LittleEndian(payload[4..]);
        int slideUntilIndex = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]);
        int layer = BinaryPrimitives.ReadInt32LittleEndian(payload[12..]);
        return index is >= 0 and <= 1024
            && slideUntilIndex is >= -1 and <= 1024
            && layer == state.Key.SubID;
    }

    private static bool IsValidHoldableEntityPayload(ReadOnlySpan<byte> payload)
        => payload.Length == 24
            && payload[0] <= (byte)WatchHoldablePhase.Gone
            && (payload[1] & ~0b0000_1111) == 0
            && payload[2] <= 8
            && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20);

    private static bool IsValidPeriodicPlatformPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 24 || payload[0] > 3 || payload[3] != 0
            || !HasFiniteSingles(payload, 4, 8, 12, 16, 20))
            return false;
        return payload[0] switch
        {
            0 => (payload[1] & ~0b0000_0011) == 0 && payload[2] == 0,
            1 => (payload[1] & ~0b0001_1111) == 0 && payload[2] <= 3,
            2 => (payload[1] & ~0b0000_1111) == 0 && payload[2] <= 2,
            3 => (payload[1] & ~0b0000_1111) == 0 && payload[2] == 0,
            _ => false,
        };
    }

    private static bool IsValidTriggerSpikesPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return payload.Length == 16
            && (payload[0] & ~0b0000_0001) == 0
            && payload[1] <= 3
            && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12);
    }

    private static bool IsValidFireBallPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return payload.Length == 24
            && (payload[0] & ~0b0001_1111) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20);
    }

    private static bool IsValidLavaPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        bool common = state.Key.EntityID == 0
            && state.Key.SubID <= 1
            && payload.Length == 40
            && (payload[0] & ~0b0011_1111) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28, 32, 36);
        if (!common)
            return false;
        return state.Key.SubID == 1
            || ((payload[0] & 0b0001_0000) == 0
                && IsZeroSingle(payload, 20)
                && IsZeroSingle(payload, 24)
                && IsZeroSingle(payload, 36));
    }

    private static bool IsValidSnowballPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 24)
            && payload[0] <= (byte)WatchSnowballPhase.Broken
            && (payload[1] & ~0b0000_0011) == 0
            && payload[2] <= 1
            && payload[2] == payload[0]
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20);
    }

    private static bool IsValidBadelineOldsitePayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID != 0)
            return IsValidBadelineOldsiteHistoryPayload(state);
        if (payload.Length != 28
            || (payload[0] & ~0b0111_1111) != 0
            || payload[1] > 38
            || !HasFiniteSingles(payload, 4, 8, 12, 16, 20))
            return false;

        float followBehindTime = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[12..])
        );
        float followBehindIndexDelay = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[16..])
        );
        return followBehindTime is >= 0f and <= 4f
            && followBehindIndexDelay is >= 0f and <= 4f
            && followBehindTime + followBehindIndexDelay <= 4f;
    }

    private static bool IsValidBadelineOldsiteHistoryPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.EntityID != 0
            || state.Key.SubID is < 1 or > 3
            || payload.Length < 4
            || payload[0] != 1
            || payload[1] + 1 != state.Key.SubID
            || payload[2] is < 1 or > 3
            || payload[1] >= payload[2]
            || payload[3] > 112
            || payload.Length != 4 + payload[3] * 9)
            return false;
        for (int offset = 4; offset < payload.Length; offset += 9)
        {
            byte packed = payload[offset + 8];
            if (!HasFiniteSingles(payload, offset, offset + 4)
                || (packed & 0b1000_0000) != 0
                || ((packed & 0b0011_1111) > 38
                    && (packed & 0b0011_1111) != 63))
                return false;
        }
        return true;
    }

    private static bool IsValidPufferPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 48)
            && payload[0] <= (byte)WatchPufferPhase.Gone
            && (payload[1] & ~0b0000_0111) == 0
            && payload[2] <= 6
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44);
    }

    private static bool IsValidAngryOshiroPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.EntityID == 0
            && HasStatePayload(state, 48)
            && payload[0] <= (byte)WatchAngryOshiroPhase.Hurt
            && (payload[1] & ~0b0011_1111) == 0
            && payload[2] <= 8
            && payload[36] <= 6
            && payload[37] == 0 && payload[38] == 0 && payload[39] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28, 40, 44)
            && ReadSingle(payload, 40) is >= 0.05f and <= 2f
            && ReadSingle(payload, 44) is >= 0f and <= 1f;
    }

    private static bool IsValidSeekerSystemPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID != 0
            || payload.Length != 44
            || payload[0] > (byte)WatchSeekerForm.Seeker
            || payload[1] > (byte)WatchSeekerPhase.Returned
            || (payload[2] & ~0b0111_1111) != 0
            || payload[3] > 15
            || payload[5] > 1 || payload[6] > 1 || payload[7] != 0
            || !HasFiniteSingles(payload, 8, 12, 16, 20, 24, 28, 32, 36))
            return false;
        return payload[0] == (byte)WatchSeekerForm.Seeker || payload[1] == 0;
    }

    private static bool IsValidSeekerBarrierPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return HasStatePayload(state, 16)
            && (payload[0] & ~0b0000_0001) == 0
            && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12);
    }

    private static bool IsValidPlayerSeekerPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID != 0
            || payload.Length != 72
            || (payload[0] & ~0b0000_1111) != 0
            || payload[1] > 15
            || payload[3] != 0
            || !HasFiniteSingles(
                payload,
                4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64
            ))
            return false;

        float dashTimer = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[20..])
        );
        float timeRate = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[48..])
        );
        float glitch = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[52..])
        );
        float anxiety = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[56..])
        );
        return dashTimer is >= -1f and <= 2f
            && timeRate is >= 0f and <= 2f
            && glitch is >= 0f and <= 1f
            && anxiety is >= 0f and <= 1f;
    }

    private static bool IsValidCassetteBlockPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (payload.Length != 24 || payload[0] > 1)
            return false;
        if (payload[0] == 0)
            return state.Key.EntityID == 0
                && state.Key.SubID == 0
                && payload[1] == 0 && payload[2] == 0 && payload[3] == 0
                && HasFiniteSingles(payload, 8, 16);
        return state.Key.SubID == 1
            && (payload[1] & ~0b0000_0111) == 0
            && payload[2] <= 3
            && payload[3] <= 3
            && HasFiniteSingles(payload, 4, 8)
            && BinaryPrimitives.ReadInt32LittleEndian(payload[12..]) is >= 0 and <= MaxCassetteBlockHeight
            && payload[16..].IndexOfAnyExcept((byte)0) < 0;
    }

    private static bool IsValidSwitchGatePayload(ReadOnlySpan<byte> payload)
        => payload.Length == 20
            && (payload[0] & ~0b0000_0111) == 0
            && payload[1] <= 3
            && HasFiniteSingles(payload, 4, 8, 12, 16);

    private static bool IsValidClutterSystemPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (payload.Length != 24
            || payload[0] > 4
            || state.Key.SubID != payload[0]
            || !HasFiniteSingles(payload, 8, 12, 16, 20))
            return false;

        if (payload[0] <= 2)
            return (payload[1] & ~0b0000_0111) == 0
                && payload[2] <= 3
                && payload[3] <= 4
                && payload[6] == 0 && payload[7] == 0;

        if (payload[0] == 3)
            return state.Key.EntityID is >= 0 and <= 2
                && payload[1] is 0 or 0b0000_0100
                && payload[2] == state.Key.EntityID
                && payload[3..].IndexOfAnyExcept((byte)0) < 0;

        return payload[1] is 0 or 0b0000_0100
            && payload[2] <= 2
            && payload[3] == 0
            && payload[4] == 0 && payload[5] == 0
            && payload[6] == 0 && payload[7] == 0
            && payload[16..].IndexOfAnyExcept((byte)0) < 0;
    }

    private static bool IsValidDoorMechanismPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return payload.Length == 16
            && payload[0] <= 2
            && state.Key.SubID == payload[0]
            && (payload[1] & ~0b0000_0111) == 0
            && payload[2] <= 3
            && payload[3] == 0 && payload[6] == 0 && payload[7] == 0
            && HasFiniteSingles(payload, 8, 12);
    }

    private static bool IsValidStrawberrySeedPayload(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID == 0)
            return payload.Length == 1 && (payload[0] & ~0b0001_1111) == 0;
        return payload.Length == 10
            && payload[0] <= (byte)WatchStrawberrySeedPhase.Combining
            && (payload[1] & ~0b0000_0111) == 0
            && HasFiniteSingles(payload, 2, 6);
    }

    private static bool IsValidBounceBlockPayload(ReadOnlySpan<byte> payload)
        => payload.Length == 60
            && (payload[0] & ~0b0001_1111) == 0
            && payload[1] <= 4
            && payload[2] == 0
            && payload[3] == 0
            && HasFiniteSingles(payload, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56);

    private static bool IsValidCrumblePlatformPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4
            || payload[0] > (byte)WatchEntityPhase.Returning
            || payload[1] > 1)
            return false;

        int imageCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        return payload.Length == 4 + (imageCount + 7) / 8;
    }

    private static bool IsValidMovingSolidPayload(ReadOnlySpan<byte> payload)
    {
        const int PayloadSize = 24;
        const byte KnownFlags = 0b0001_1111;
        if (payload.Length != PayloadSize
            || payload[0] > (byte)WatchMovingSolidType.StarJumpBlock
            || (payload[1] & ~KnownFlags) != 0
            || payload[3] != 0
            || !HasFiniteSingles(payload, 4, 8, 12, 16, 20))
            return false;

        WatchMovingSolidType type = (WatchMovingSolidType)payload[0];
        byte state = payload[2];
        return type switch
        {
            WatchMovingSolidType.SwapBlock => state <= 1,
            WatchMovingSolidType.MoveBlock => state <= 2,
            WatchMovingSolidType.BounceBlock => false,
            _ => state == 0,
        };
    }

    private static bool HasFiniteSingles(ReadOnlySpan<byte> payload, params ReadOnlySpan<int> offsets)
    {
        foreach (int offset in offsets)
        {
            if (offset < 0 || offset + sizeof(float) > payload.Length)
                return false;
            float value = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(payload[offset..])
            );
            if (!float.IsFinite(value))
                return false;
        }
        return true;
    }

    private static bool IsZeroSingle(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]) is 0 or int.MinValue;

    private static int GetEntityKeySerializedSize()
        => sizeof(ushort) + sizeof(int) + sizeof(ushort);

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
