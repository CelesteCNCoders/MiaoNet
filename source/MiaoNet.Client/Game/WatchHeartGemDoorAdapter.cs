using System.Runtime.CompilerServices;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchHeartGemDoorAdapter : IWatchEntityAdapter
{
    private sealed class RemoteDoorState
    {
        public byte[] Payload { get; set; } = [];
    }

    private static readonly WatchHeartGemDoorAdapter instance = new();
    private static readonly ConditionalWeakTable<HeartGemDoor, RemoteDoorState> remoteStates = new();

    public WatchEntityKind Kind => WatchEntityKind.HeartGemDoor;

    public static void Load()
    {
        On.Celeste.HeartGemDoor.ctor += HeartGemDoor_ctor;
        On.Celeste.HeartGemDoor.Update += HeartGemDoor_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.HeartGemDoor.Update -= HeartGemDoor_Update;
        On.Celeste.HeartGemDoor.ctor -= HeartGemDoor_ctor;
        WatchEntityIDTable<HeartGemDoor>.Clear();
        remoteStates.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (HeartGemDoor door in WatchRoomEntityIndex.Enumerate<HeartGemDoor>(level))
        {
            if (!WatchEntityIDTable<HeartGemDoor>.TryGet(door, room, out int id))
                continue;

            var current = (
                door.Opened,
                door.Counter,
                door.openPercent,
                TopY: door.TopSolid.Position.Y,
                BottomY: door.BotSolid.Position.Y,
                TopCollidable: door.TopSolid.Collidable,
                BottomCollidable: door.BotSolid.Collidable,
                door.Visible,
                door.heartAlpha
            );
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, 24,
                static (payload, state) =>
                {
                    payload[0] = state.Opened ? (byte)1 : (byte)0;
                    WatchEntityPayloadCodec.WriteSingle(payload, 1, state.Counter);
                    WatchEntityPayloadCodec.WriteSingle(payload, 5, state.openPercent);
                    WatchEntityPayloadCodec.WriteSingle(payload, 9, state.TopY);
                    WatchEntityPayloadCodec.WriteSingle(payload, 13, state.BottomY);
                    payload[17] = state.TopCollidable ? (byte)1 : (byte)0;
                    payload[18] = state.BottomCollidable ? (byte)1 : (byte)0;
                    payload[19] = state.Visible ? (byte)1 : (byte)0;
                    WatchEntityPayloadCodec.WriteSingle(payload, 20, state.heartAlpha);
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
        Dictionary<int, WatchEntityState> desiredByID = new();
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || payload.Length != 24
                || payload[0] > 1
                || payload[17] > 1
                || payload[18] > 1
                || payload[19] > 1
                || !HasFiniteDoorValues(payload)
                || !desiredByID.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (HeartGemDoor door in WatchRoomEntityIndex.Enumerate<HeartGemDoor>(level))
        {
            if (!WatchEntityIDTable<HeartGemDoor>.TryGet(door, room, out int id)
                || !desiredByID.TryGetValue(id, out WatchEntityState state))
            {
                if (isCompleteState)
                    remoteStates.Remove(door);
                continue;
            }

            ReadOnlySpan<byte> payload = state.Payload.Span;
            bool opened = payload[0] != 0;
            float counter = WatchEntityPayloadCodec.ReadSingle(payload, 1);
            float openPercent = WatchEntityPayloadCodec.ReadSingle(payload, 5);
            float topY = WatchEntityPayloadCodec.ReadSingle(payload, 9);
            float botY = WatchEntityPayloadCodec.ReadSingle(payload, 13);
            bool topCollidable = payload[17] != 0;
            bool botCollidable = payload[18] != 0;
            bool visible = payload[19] != 0;
            float heartAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 20);
            bool differs = door.Opened != opened
                || door.Counter != counter
                || door.openPercent != openPercent
                || door.TopSolid.Position.Y != topY
                || door.BotSolid.Position.Y != botY
                || door.TopSolid.Collidable != topCollidable
                || door.BotSolid.Collidable != botCollidable
                || door.Visible != visible
                || door.heartAlpha != heartAlpha;

            if (!remoteStates.TryGetValue(door, out RemoteDoorState? remoteState))
            {
                remoteState = new RemoteDoorState();
                remoteStates.Add(door, remoteState);
            }
            remoteState.Payload = payload.ToArray();
            if (!differs)
                continue;

            if (!isCompleteState && (int)counter > (int)door.Counter)
                Audio.Play("event:/game/09_core/frontdoor_heartfill", door.Position);
            if (!isCompleteState && !door.Opened && opened)
            {
                level.Shake(0.3f);
                level.Flash(Color.White * 0.5f);
                Audio.Play("event:/game/09_core/frontdoor_unlock", door.Position);
            }

            if (!door.Opened && opened)
                door.offset = 0f;

            ApplyPayload(door, payload);
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void HeartGemDoor_ctor(
        On.Celeste.HeartGemDoor.orig_ctor orig,
        HeartGemDoor self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<HeartGemDoor>.Set(self, data.Level.Name, data.ID);
    }

    private static void HeartGemDoor_Update(
        On.Celeste.HeartGemDoor.orig_Update orig,
        HeartGemDoor self
    )
    {
        orig(self);
        if (MiaoNetModule.IsWatching
            && remoteStates.TryGetValue(self, out RemoteDoorState? state)
            && state.Payload.Length == 24)
            ApplyPayload(self, state.Payload);
    }

    private static void ApplyPayload(HeartGemDoor door, ReadOnlySpan<byte> payload)
    {
        door.Opened = payload[0] != 0;
        door.Counter = WatchEntityPayloadCodec.ReadSingle(payload, 1);
        door.openPercent = WatchEntityPayloadCodec.ReadSingle(payload, 5);
        door.TopSolid.Position = new Vector2(
            door.TopSolid.Position.X,
            WatchEntityPayloadCodec.ReadSingle(payload, 9)
        );
        door.BotSolid.Position = new Vector2(
            door.BotSolid.Position.X,
            WatchEntityPayloadCodec.ReadSingle(payload, 13)
        );
        door.TopSolid.Collidable = payload[17] != 0;
        door.BotSolid.Collidable = payload[18] != 0;
        door.Visible = payload[19] != 0;
        door.heartAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 20);
    }

    private static bool HasFiniteDoorValues(ReadOnlySpan<byte> payload)
    {
        foreach (int offset in new[] { 1, 5, 9, 13, 20 })
        {
            if (!float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, offset)))
                return false;
        }
        return true;
    }
}
