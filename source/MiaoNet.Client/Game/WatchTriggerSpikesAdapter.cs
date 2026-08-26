using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchTriggerSpikesAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 16;
    private const byte TriggeredFlag = 1 << 0;
    private const byte TriggerEvent = 1;

    private readonly record struct SpikeKey(int EntityID, ushort SubID);
    private readonly record struct SpikeState(
        bool Triggered,
        byte Direction,
        float Lerp,
        float DelayTimer,
        float RetractTimer
    );

    private static readonly WatchTriggerSpikesAdapter instance = new();
    private static readonly Dictionary<SpikeKey, SpikeState> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.TriggerSpikes;

    public static void Load()
    {
        On.Celeste.TriggerSpikes.ctor_EntityData_Vector2_Directions += TriggerSpikes_ctor;
        On.Celeste.TriggerSpikes.OnCollide += TriggerSpikes_OnCollide;
        On.Celeste.TriggerSpikes.Update += TriggerSpikes_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.TriggerSpikes.Update -= TriggerSpikes_Update;
        On.Celeste.TriggerSpikes.OnCollide -= TriggerSpikes_OnCollide;
        On.Celeste.TriggerSpikes.ctor_EntityData_Vector2_Directions -= TriggerSpikes_ctor;
        WatchEntityIDTable<TriggerSpikes>.Clear();
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (TriggerSpikes parent in level.Entities.OfType<TriggerSpikes>())
        {
            if (!WatchEntityIDTable<TriggerSpikes>.TryGet(parent, room, out int id))
                continue;
            for (int i = 0; i < parent.spikes.Length && i <= ushort.MaxValue; i++)
                yield return Encode(id, (ushort)i, parent.direction, parent.spikes[i]);
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
            if (!TryDecode(state, out SpikeKey key, out SpikeState desired)
                || !remoteStates.TryAdd(key, desired))
            {
                if (!isCompleteState && TryDecode(state, out key, out desired))
                    remoteStates[key] = desired;
                else
                    return WatchEntityApplyResult.None;
            }
        }

        bool changed = false;
        bool requiresReload = false;
        HashSet<SpikeKey> found = new();
        foreach (TriggerSpikes parent in level.Entities.OfType<TriggerSpikes>())
        {
            if (!WatchEntityIDTable<TriggerSpikes>.TryGet(parent, room, out int id))
                continue;
            for (int i = 0; i < parent.spikes.Length && i <= ushort.MaxValue; i++)
            {
                SpikeKey key = new(id, (ushort)i);
                found.Add(key);
                if (remoteStates.TryGetValue(key, out SpikeState desired))
                {
                    if ((byte)parent.direction != desired.Direction)
                        requiresReload = true;
                    else
                        changed |= Apply(parent, i, desired);
                }
                else if (isCompleteState)
                    requiresReload = true;
            }
        }
        if (remoteStates.Keys.Any(key => !found.Contains(key)))
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
            || entityEvent.EventID != TriggerEvent
            || entityEvent.Payload.Length != 0)
            return;

        string room = level.Session.Level;
        TriggerSpikes? parent = WatchEntityIDTable<TriggerSpikes>.Find(
            level,
            room,
            entityEvent.Key.EntityID
        );
        if (parent is null || entityEvent.Key.SubID >= parent.spikes.Length)
            return;
        Audio.Play(
            "event:/game/03_resort/fluff_tendril_touch",
            parent.spikes[entityEvent.Key.SubID].WorldPosition
        );
    }

    private static WatchEntityState Encode(
        int id,
        ushort subID,
        TriggerSpikes.Directions direction,
        TriggerSpikes.SpikeInfo spike
    )
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = spike.Triggered ? TriggeredFlag : (byte)0;
        payload[1] = (byte)direction;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, spike.Lerp);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, spike.DelayTimer);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, spike.RetractTimer);
        return new(new WatchEntityKey(WatchEntityKind.TriggerSpikes, id, subID), payload);
    }

    private static bool TryDecode(
        WatchEntityState state,
        out SpikeKey key,
        out SpikeState desired
    )
    {
        key = default;
        desired = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.TriggerSpikes
            || payload.Length != PayloadSize
            || (payload[0] & ~TriggeredFlag) != 0
            || payload[1] > (byte)TriggerSpikes.Directions.Right
            || payload[2] != 0 || payload[3] != 0)
            return false;

        float lerp = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        float delay = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        float retract = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        if (!float.IsFinite(lerp) || !float.IsFinite(delay) || !float.IsFinite(retract))
            return false;
        key = new(state.Key.EntityID, state.Key.SubID);
        desired = new((payload[0] & TriggeredFlag) != 0, payload[1], lerp, delay, retract);
        return true;
    }

    private static bool Apply(TriggerSpikes parent, int index, SpikeState desired)
    {
        if ((byte)parent.direction != desired.Direction)
            return false;

        TriggerSpikes.SpikeInfo spike = parent.spikes[index];
        bool changed = spike.Triggered != desired.Triggered
            || spike.Lerp != desired.Lerp
            || spike.DelayTimer != desired.DelayTimer
            || spike.RetractTimer != desired.RetractTimer;
        spike.Triggered = desired.Triggered;
        spike.Lerp = desired.Lerp;
        spike.DelayTimer = desired.DelayTimer;
        spike.RetractTimer = desired.RetractTimer;
        parent.spikes[index] = spike;
        return changed;
    }

    private static void TriggerSpikes_ctor(
        On.Celeste.TriggerSpikes.orig_ctor_EntityData_Vector2_Directions orig,
        TriggerSpikes self,
        EntityData data,
        Vector2 offset,
        TriggerSpikes.Directions direction
    )
    {
        orig(self, data, offset, direction);
        WatchEntityIDTable<TriggerSpikes>.Set(self, data.Level.Name, data.ID);
    }

    private static void TriggerSpikes_OnCollide(
        On.Celeste.TriggerSpikes.orig_OnCollide orig,
        TriggerSpikes self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;

        bool[] triggeredBefore = self.spikes.Select(spike => spike.Triggered).ToArray();
        orig(self, player);
        if (self.Scene is not Level level
            || !WatchEntityIDTable<TriggerSpikes>.TryGet(self, level.Session.Level, out int id))
            return;
        for (int i = 0; i < self.spikes.Length && i <= ushort.MaxValue; i++)
        {
            if (triggeredBefore[i] || !self.spikes[i].Triggered)
                continue;
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(
                    new WatchEntityKey(WatchEntityKind.TriggerSpikes, id, (ushort)i),
                    TriggerEvent,
                    []
                )
            );
        }
    }

    private static void TriggerSpikes_Update(
        On.Celeste.TriggerSpikes.orig_Update orig,
        TriggerSpikes self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }

        self.Components.Update();
        for (int i = 0; i < self.spikes.Length; i++)
        {
            TriggerSpikes.SpikeInfo spike = self.spikes[i];
            if (spike.Triggered)
                spike.TextureRotation += Engine.DeltaTime * 1.2f;
            else
                spike.TentacleFrame += Engine.DeltaTime * 12f;
            self.spikes[i] = spike;
        }
    }
}
