using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchDashSwitchAdapter : IWatchEntityAdapter
{
    private static readonly WatchDashSwitchAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.DashSwitch;

    public static void Load()
        => WatchEntitySyncRegistry.Register(instance);

    public static void Unload()
        => WatchEntitySyncRegistry.Unregister(instance);

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (DashSwitch dashSwitch in WatchRoomEntityIndex.Enumerate<DashSwitch>(level))
        {
            if (!StringComparer.Ordinal.Equals(dashSwitch.id.Level, level.Session.Level))
                continue;

            var current = (dashSwitch.pressed, dashSwitch.Position);
            yield return WatchEntityState.FromTyped(
                new(Kind, dashSwitch.id.ID), current, 9,
                static (payload, state) =>
                {
                    payload[0] = state.pressed ? (byte)1 : (byte)0;
                    WatchEntityPayloadCodec.WriteVector2(payload, 1, state.Position);
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
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 9
                || state.Payload.Span[0] > 1
                || !desiredByID.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        foreach (DashSwitch dashSwitch in WatchRoomEntityIndex.Enumerate<DashSwitch>(level))
        {
            if (!StringComparer.Ordinal.Equals(dashSwitch.id.Level, level.Session.Level)
                || !desiredByID.TryGetValue(dashSwitch.id.ID, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            bool pressed = payload[0] != 0;
            Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 1);
            bool differs = dashSwitch.pressed != pressed || dashSwitch.Position != position;
            if (!differs)
                continue;

            if (!dashSwitch.pressed && pressed)
            {
                dashSwitch.sprite.Play("push");
                Audio.Play("event:/game/05_mirror_temple/button_activate", dashSwitch.Position);
            }
            else if (dashSwitch.pressed && !pressed)
                dashSwitch.sprite.Play("idle");

            dashSwitch.pressed = pressed;
            dashSwitch.Position = position;
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

}

internal sealed class WatchTempleGateAdapter : IWatchEntityAdapter
{
    private sealed class RemoteGateState
    {
        public bool HasState { get; set; }
        public bool Open { get; set; }
    }

    private static readonly WatchTempleGateAdapter instance = new();
    private static readonly ConditionalWeakTable<TempleGate, RemoteGateState> remoteStates = new();

    public WatchEntityKind Kind => WatchEntityKind.TempleGate;

    public static void Load()
    {
        On.Celeste.TempleGate.ctor_EntityData_Vector2_string += TempleGate_ctor;
        On.Celeste.TempleGate.TheoIsNearby += TempleGate_TheoIsNearby;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.TempleGate.TheoIsNearby -= TempleGate_TheoIsNearby;
        On.Celeste.TempleGate.ctor_EntityData_Vector2_string -= TempleGate_ctor;
        remoteStates.Clear();
        WatchEntityIDTable<TempleGate>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (TempleGate gate in WatchRoomEntityIndex.Enumerate<TempleGate>(level))
        {
            if (!WatchEntityIDTable<TempleGate>.TryGet(gate, room, out int id))
                continue;

            var current = (gate.open, gate.drawHeight, gate.Collidable);
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, 6,
                static (payload, state) =>
                {
                    payload[0] = state.open ? (byte)1 : (byte)0;
                    WatchEntityPayloadCodec.WriteSingle(payload, 1, state.drawHeight);
                    payload[5] = state.Collidable ? (byte)1 : (byte)0;
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
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 6
                || state.Payload.Span[0] > 1
                || state.Payload.Span[5] > 1
                || !desiredByID.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (TempleGate gate in WatchRoomEntityIndex.Enumerate<TempleGate>(level))
        {
            if (!WatchEntityIDTable<TempleGate>.TryGet(gate, room, out int id)
                || !desiredByID.TryGetValue(id, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            bool open = payload[0] != 0;
            float drawHeight = WatchEntityPayloadCodec.ReadSingle(payload, 1);
            bool collidable = payload[5] != 0;
            RemoteGateState remoteState = remoteStates.GetOrCreateValue(gate);
            remoteState.HasState = true;
            remoteState.Open = open;
            if (gate.open == open
                && gate.drawHeight == drawHeight
                && gate.Collidable == collidable)
                continue;

            if (!gate.open && open)
            {
                gate.sprite.Play("open");
                Audio.Play(
                    gate.theoGate
                        ? "event:/game/05_mirror_temple/gate_theo_open"
                        : "event:/game/05_mirror_temple/gate_main_open",
                    gate.Position
                );
            }
            else if (gate.open && !open)
                gate.sprite.Play("idle");

            gate.open = open;
            gate.drawHeight = drawHeight;
            gate.SetHeight(Math.Max(0, (int)MathF.Round(drawHeight)));
            gate.Collidable = collidable;
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static bool TempleGate_TheoIsNearby(
        On.Celeste.TempleGate.orig_TheoIsNearby orig,
        TempleGate self
    )
    {
        if (MiaoNetModule.IsWatching
            && remoteStates.TryGetValue(self, out RemoteGateState? state)
            && state.HasState)
            return state.Open;

        return orig(self);
    }

    private static void TempleGate_ctor(
        On.Celeste.TempleGate.orig_ctor_EntityData_Vector2_string orig,
        TempleGate self,
        EntityData data,
        Vector2 offset,
        string levelID
    )
    {
        orig(self, data, offset, levelID);
        WatchEntityIDTable<TempleGate>.Set(self, data.Level.Name, data.ID);
    }
}
