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
        foreach (DashSwitch dashSwitch in level.Entities.OfType<DashSwitch>())
        {
            if (!StringComparer.Ordinal.Equals(dashSwitch.id.Level, level.Session.Level))
                continue;

            byte[] payload = new byte[9];
            payload[0] = dashSwitch.pressed ? (byte)1 : (byte)0;
            WatchEntityPayloadCodec.WriteSingle(payload, 1, dashSwitch.Position.X);
            WatchEntityPayloadCodec.WriteSingle(payload, 5, dashSwitch.Position.Y);
            yield return new WatchEntityState(
                new WatchEntityKey(Kind, dashSwitch.id.ID),
                payload
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
        foreach (DashSwitch dashSwitch in level.Entities.OfType<DashSwitch>())
        {
            if (!StringComparer.Ordinal.Equals(dashSwitch.id.Level, level.Session.Level)
                || !desiredByID.TryGetValue(dashSwitch.id.ID, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            bool pressed = payload[0] != 0;
            Vector2 position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 1),
                WatchEntityPayloadCodec.ReadSingle(payload, 5)
            );
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

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
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
        foreach (TempleGate gate in level.Entities.OfType<TempleGate>())
        {
            if (!WatchEntityIDTable<TempleGate>.TryGet(gate, room, out int id))
                continue;

            byte[] payload = new byte[6];
            payload[0] = gate.open ? (byte)1 : (byte)0;
            WatchEntityPayloadCodec.WriteSingle(payload, 1, gate.drawHeight);
            payload[5] = gate.Collidable ? (byte)1 : (byte)0;
            yield return new WatchEntityState(new WatchEntityKey(Kind, id), payload);
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
        foreach (TempleGate gate in level.Entities.OfType<TempleGate>())
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

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
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
