using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchDoorMechanismAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 16;
    private const byte DoorType = 0;
    private const byte TrapdoorType = 1;
    private const byte OshiroDoorType = 2;
    private const byte DoorOpenEvent = 1;
    private const byte TrapdoorOpenEvent = 2;
    private const byte OshiroDoorOpenEvent = 3;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte BoolFlag = 1 << 2;

    private static readonly WatchDoorMechanismAdapter instance = new();
    private static readonly Dictionary<WatchEntityKey, byte[]> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.DoorMechanism;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        On.Celeste.Door.ctor += Door_ctor;
        On.Celeste.Door.Open += Door_Open;
        On.Celeste.Trapdoor.ctor += Trapdoor_ctor;
        On.Celeste.Trapdoor.Open += Trapdoor_Open;
        On.Celeste.MrOshiroDoor.ctor += MrOshiroDoor_ctor;
        On.Celeste.MrOshiroDoor.Open += MrOshiroDoor_Open;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.MrOshiroDoor.Open -= MrOshiroDoor_Open;
        On.Celeste.MrOshiroDoor.ctor -= MrOshiroDoor_ctor;
        On.Celeste.Trapdoor.Open -= Trapdoor_Open;
        On.Celeste.Trapdoor.ctor -= Trapdoor_ctor;
        On.Celeste.Door.Open -= Door_Open;
        On.Celeste.Door.ctor -= Door_ctor;
        On.Celeste.Level.Update -= Level_Update;
        WatchEntityIDTable<Door>.Clear();
        WatchEntityIDTable<Trapdoor>.Clear();
        WatchEntityIDTable<MrOshiroDoor>.Clear();
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Door door in level.Entities.OfType<Door>())
        {
            if (WatchEntityIDTable<Door>.TryGet(door, room, out int id))
                yield return Encode(
                    new WatchEntityKey(Kind, id, DoorType),
                    DoorType,
                    Flags(door, door.disabled),
                    door.sprite,
                    door.sprite.Scale.X,
                    0f
                );
        }
        foreach (Trapdoor door in level.Entities.OfType<Trapdoor>())
        {
            if (WatchEntityIDTable<Trapdoor>.TryGet(door, room, out int id))
                yield return Encode(
                    new WatchEntityKey(Kind, id, TrapdoorType),
                    TrapdoorType,
                    Flags(door, door.occluder.Visible),
                    door.sprite,
                    0f,
                    0f
                );
        }
        foreach (MrOshiroDoor door in level.Entities.OfType<MrOshiroDoor>())
        {
            if (WatchEntityIDTable<MrOshiroDoor>.TryGet(door, room, out int id))
                yield return Encode(
                    new WatchEntityKey(Kind, id, OshiroDoorType),
                    OshiroDoorType,
                    Flags(door, false),
                    door.sprite,
                    door.wiggler.Value,
                    0f
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
        bool reload = false;
        if (isCompleteState)
        {
            HashSet<WatchEntityKey> local = Enumerate(level).Select(pair => pair.Key).ToHashSet();
            reload = remoteStates.Keys.Any(key => !local.Contains(key))
                || local.Any(key => !remoteStates.ContainsKey(key));
        }
        return (changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None)
            | (reload ? WatchEntityApplyResult.RequiresRoomReload : WatchEntityApplyResult.None);
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        Entity? entity = Enumerate(level)
            .FirstOrDefault(pair => pair.Key == entityEvent.Key).Entity;
        if (entity is null)
            return;
        switch (entityEvent.EventID)
        {
            case DoorOpenEvent when entity is Door door && entityEvent.Payload.Length == 4:
                door.Open(WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0));
                break;
            case TrapdoorOpenEvent when entity is Trapdoor trapdoor && entityEvent.Payload.Length == 1:
                trapdoor.Collidable = false;
                trapdoor.occluder.Visible = false;
                if (entityEvent.Payload.Span[0] != 0)
                    trapdoor.Add(new Coroutine(trapdoor.OpenFromBottom()));
                else
                {
                    Audio.Play("event:/game/03_resort/trapdoor_fromtop", trapdoor.Position);
                    trapdoor.sprite.Play("open");
                }
                break;
            case OshiroDoorOpenEvent when entity is MrOshiroDoor oshiroDoor && entityEvent.Payload.Length == 0:
                oshiroDoor.Open();
                break;
        }
    }

    private static bool ApplyRemote(Level level)
    {
        bool changed = false;
        foreach ((Entity entity, WatchEntityKey key) in Enumerate(level))
        {
            if (!remoteStates.TryGetValue(key, out byte[]? payload))
                continue;
            bool visible = (payload[1] & VisibleFlag) != 0;
            bool collidable = (payload[1] & CollidableFlag) != 0;
            bool boolValue = (payload[1] & BoolFlag) != 0;
            changed |= entity.Visible != visible || entity.Collidable != collidable;
            entity.Visible = visible;
            entity.Collidable = collidable;
            Sprite sprite;
            switch (entity)
            {
                case Door door:
                    changed |= door.disabled != boolValue;
                    door.disabled = boolValue;
                    door.sprite.Scale.X = WatchEntityPayloadCodec.ReadSingle(payload, 8);
                    sprite = door.sprite;
                    break;
                case Trapdoor trapdoor:
                    changed |= trapdoor.occluder.Visible != boolValue;
                    trapdoor.occluder.Visible = boolValue;
                    sprite = trapdoor.sprite;
                    break;
                case MrOshiroDoor oshiroDoor:
                    oshiroDoor.wiggler.Value = WatchEntityPayloadCodec.ReadSingle(payload, 8);
                    sprite = oshiroDoor.sprite;
                    break;
                default:
                    continue;
            }
            ApplyAnimation(sprite, payload[2], WatchEntityPayloadCodec.ReadUInt16(payload, 4));
        }
        return changed;
    }

    private static IEnumerable<(Entity Entity, WatchEntityKey Key)> Enumerate(Level level)
    {
        string room = level.Session.Level;
        foreach (Door door in level.Entities.OfType<Door>())
            if (WatchEntityIDTable<Door>.TryGet(door, room, out int id))
                yield return (door, new WatchEntityKey(WatchEntityKind.DoorMechanism, id, DoorType));
        foreach (Trapdoor door in level.Entities.OfType<Trapdoor>())
            if (WatchEntityIDTable<Trapdoor>.TryGet(door, room, out int id))
                yield return (door, new WatchEntityKey(WatchEntityKind.DoorMechanism, id, TrapdoorType));
        foreach (MrOshiroDoor door in level.Entities.OfType<MrOshiroDoor>())
            if (WatchEntityIDTable<MrOshiroDoor>.TryGet(door, room, out int id))
                yield return (door, new WatchEntityKey(WatchEntityKind.DoorMechanism, id, OshiroDoorType));
    }

    private static byte Flags(Entity entity, bool boolValue)
        => (byte)((entity.Visible ? VisibleFlag : 0)
            | (entity.Collidable ? CollidableFlag : 0)
            | (boolValue ? BoolFlag : 0));

    private static WatchEntityState Encode(
        WatchEntityKey key,
        byte type,
        byte flags,
        Sprite sprite,
        float value0,
        float value1
    )
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = type;
        payload[1] = flags;
        payload[2] = EncodeAnimation(sprite.CurrentAnimationID);
        WatchEntityPayloadCodec.WriteUInt16(payload, 4, (ushort)Math.Max(0, sprite.CurrentAnimationFrame));
        WatchEntityPayloadCodec.WriteSingle(payload, 8, value0);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, value1);
        return new WatchEntityState(key, payload);
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.DoorMechanism
            && payload.Length == PayloadSize
            && payload[0] <= OshiroDoorType
            && state.Key.SubID == payload[0]
            && (payload[1] & ~(VisibleFlag | CollidableFlag | BoolFlag)) == 0
            && payload[2] <= 3
            && payload[3] == 0 && payload[6] == 0 && payload[7] == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 12));
    }

    private static byte EncodeAnimation(string animation)
        => animation switch { "idle" => 1, "open" => 2, "close" => 3, _ => 0 };

    private static void ApplyAnimation(Sprite sprite, byte animation, int frame)
    {
        string? id = animation switch { 1 => "idle", 2 => "open", 3 => "close", _ => null };
        if (id is null)
            return;
        if (sprite.CurrentAnimationID != id)
            sprite.Play(id);
        sprite.SetAnimationFrame(frame);
    }

    private static void Publish(Entity entity, byte subtype, int id, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (entity.Scene is Level level && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.DoorMechanism, id, subtype), eventID, payload)
            );
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if (MiaoNetModule.IsWatching && StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            ApplyRemote(self);
    }

    private static void Door_ctor(On.Celeste.Door.orig_ctor orig, Door self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        WatchEntityIDTable<Door>.Set(self, data.Level.Name, data.ID);
    }

    private static void Door_Open(On.Celeste.Door.orig_Open orig, Door self, float fromX)
    {
        orig(self, fromX);
        if (self.Scene is Level level
            && WatchEntityIDTable<Door>.TryGet(self, level.Session.Level, out int id))
        {
            Span<byte> payload = stackalloc byte[4];
            WatchEntityPayloadCodec.WriteSingle(payload, 0, fromX);
            Publish(self, DoorType, id, DoorOpenEvent, payload);
        }
    }

    private static void Trapdoor_ctor(On.Celeste.Trapdoor.orig_ctor orig, Trapdoor self, EntityData data, Vector2 offset)
    {
        orig(self, data, offset);
        WatchEntityIDTable<Trapdoor>.Set(self, data.Level.Name, data.ID);
    }

    private static void Trapdoor_Open(On.Celeste.Trapdoor.orig_Open orig, Trapdoor self, Player player)
    {
        bool fromBottom = player.Speed.Y < 0f;
        orig(self, player);
        if (self.Scene is Level level
            && WatchEntityIDTable<Trapdoor>.TryGet(self, level.Session.Level, out int id))
        {
            Span<byte> payload = stackalloc byte[1];
            payload[0] = fromBottom ? (byte)1 : (byte)0;
            Publish(self, TrapdoorType, id, TrapdoorOpenEvent, payload);
        }
    }

    private static void MrOshiroDoor_ctor(
        On.Celeste.MrOshiroDoor.orig_ctor orig,
        MrOshiroDoor self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<MrOshiroDoor>.Set(self, data.Level.Name, data.ID);
    }

    private static void MrOshiroDoor_Open(On.Celeste.MrOshiroDoor.orig_Open orig, MrOshiroDoor self)
    {
        orig(self);
        if (self.Scene is Level level
            && WatchEntityIDTable<MrOshiroDoor>.TryGet(self, level.Session.Level, out int id))
            Publish(self, OshiroDoorType, id, OshiroDoorOpenEvent, []);
    }
}
