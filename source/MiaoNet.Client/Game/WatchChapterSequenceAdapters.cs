using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchBadelineBoostAdapter : IWatchEntityAdapter
{
    private const byte ActivateEvent = 1;
    private const int PayloadSize = 16;
    private const byte EntityVisibleFlag = 1 << 0;
    private const byte TravellingFlag = 1 << 1;
    private const byte HoldingFlag = 1 << 2;
    private const byte SpriteVisibleFlag = 1 << 3;
    private const byte StretchVisibleFlag = 1 << 4;

    private sealed class Info
    {
        public string Level { get; }
        public int ID { get; }
        public Info(string level, int id) { Level = level; ID = id; }
    }

    private static readonly WatchBadelineBoostAdapter instance = new();
    private static readonly ConditionalWeakTable<BadelineBoost, Info> infos = new();

    public WatchEntityKind Kind => WatchEntityKind.BadelineBoost;

    public static void Load()
    {
        On.Celeste.BadelineBoost.ctor_EntityData_Vector2 += BadelineBoost_ctor;
        On.Celeste.BadelineBoost.OnPlayer += BadelineBoost_OnPlayer;
        On.Celeste.BadelineBoost.Update += BadelineBoost_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BadelineBoost.Update -= BadelineBoost_Update;
        On.Celeste.BadelineBoost.OnPlayer -= BadelineBoost_OnPlayer;
        On.Celeste.BadelineBoost.ctor_EntityData_Vector2 -= BadelineBoost_ctor;
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (BadelineBoost boost in level.Entities.OfType<BadelineBoost>().ToArray())
        {
            if (!infos.TryGetValue(boost, out Info? info) || info.Level != level.Session.Level)
                continue;
            WatchEntityPhase phase = boost.holding is not null
                ? WatchEntityPhase.Active
                : boost.travelling
                    ? WatchEntityPhase.Returning
                    : boost.Visible ? WatchEntityPhase.Ready : WatchEntityPhase.Gone;
            byte[] payload = new byte[PayloadSize];
            payload[0] = (byte)phase;
            if (boost.Visible) payload[1] |= EntityVisibleFlag;
            if (boost.travelling) payload[1] |= TravellingFlag;
            if (boost.holding is not null) payload[1] |= HoldingFlag;
            if (boost.sprite.Visible) payload[1] |= SpriteVisibleFlag;
            if (boost.stretch.Visible) payload[1] |= StretchVisibleFlag;
            WatchEntityPayloadCodec.WriteUInt16(payload, 2, checked((ushort)Math.Clamp(boost.nodeIndex, 0, ushort.MaxValue)));
            WatchEntityPayloadCodec.WriteSingle(payload, 4, boost.Position.X);
            WatchEntityPayloadCodec.WriteSingle(payload, 8, boost.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(payload, 12, GetStretchProgress(boost));
            yield return new(new WatchEntityKey(Kind, info.ID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (BadelineBoost boost in level.Entities.OfType<BadelineBoost>().ToArray())
        {
            if (!infos.TryGetValue(boost, out Info? info) || info.Level != level.Session.Level)
                continue;
            if (!desired.TryGetValue(info.ID, out WatchEntityState state))
            {
                if (isCompleteState)
                {
                    boost.RemoveSelf();
                    changed = true;
                }
                continue;
            }
            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase phase = (WatchEntityPhase)payload[0];
            boost.nodeIndex = Math.Min(
                WatchEntityPayloadCodec.ReadUInt16(payload, 2),
                Math.Max(0, boost.nodes.Length - 1)
            );
            boost.Position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 4),
                WatchEntityPayloadCodec.ReadSingle(payload, 8));
            boost.travelling = (payload[1] & TravellingFlag) != 0;
            boost.holding = null;
            boost.Visible = phase != WatchEntityPhase.Gone
                && (payload[1] & EntityVisibleFlag) != 0;
            boost.sprite.Visible = (payload[1] & SpriteVisibleFlag) != 0;
            ApplyStretchPresentation(
                boost,
                (payload[1] & StretchVisibleFlag) != 0,
                WatchEntityPayloadCodec.ReadSingle(payload, 12)
            );
            // The watched client is the only authority for activation. The
            // hidden transport Player must never start a local BoostRoutine.
            boost.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ActivateEvent || entityEvent.Payload.Length != 0)
            return;
        BadelineBoost? boost = level.Entities.OfType<BadelineBoost>().FirstOrDefault(candidate =>
            infos.TryGetValue(candidate, out Info? info)
            && info.Level == level.Session.Level && info.ID == entityEvent.Key.EntityID);
        if (boost is null)
            return;
        boost.Wiggle();
        boost.Collidable = false;
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.BadelineBoost && state.Key.SubID == 0
            && payload.Length == PayloadSize
            && payload[0] <= (byte)WatchEntityPhase.Returning
            && (payload[1] & ~0b0001_1111) == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 4))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && WatchEntityPayloadCodec.ReadSingle(payload, 12) is >= 0f and <= 1f;
    }

    private static float GetStretchProgress(BadelineBoost boost)
    {
        if (!boost.travelling || !boost.stretch.Visible
            || boost.nodeIndex <= 0 || boost.nodeIndex >= boost.nodes.Length)
            return 0f;

        Vector2 from = boost.nodes[boost.nodeIndex - 1];
        Vector2 delta = boost.nodes[boost.nodeIndex] - from;
        float lengthSquared = delta.LengthSquared();
        return lengthSquared <= 0.0001f
            ? 1f
            : Math.Clamp(Vector2.Dot(boost.Position - from, delta) / lengthSquared, 0f, 1f);
    }

    private static void ApplyStretchPresentation(
        BadelineBoost boost,
        bool visible,
        float easedProgress
    )
    {
        visible &= boost.nodeIndex > 0 && boost.nodeIndex < boost.nodes.Length;
        boost.stretch.Visible = visible;
        if (!visible)
            return;

        Vector2 from = boost.nodes[boost.nodeIndex - 1];
        Vector2 to = boost.nodes[boost.nodeIndex];
        float stretch = Calc.YoYo(easedProgress);
        boost.stretch.Rotation = Calc.Angle(to - from);
        boost.stretch.Scale.X = 1f + stretch * 2f;
        boost.stretch.Scale.Y = 1f - stretch * 0.75f;
    }

    private static void BadelineBoost_ctor(
        On.Celeste.BadelineBoost.orig_ctor_EntityData_Vector2 orig,
        BadelineBoost self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new Info(data.Level.Name, data.ID));
    }

    private static void BadelineBoost_OnPlayer(
        On.Celeste.BadelineBoost.orig_OnPlayer orig,
        BadelineBoost self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching)
            return;

        bool wasAvailable = self.Collidable;
        orig(self, player);
        if (!wasAvailable || self.Collidable || WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level || !infos.TryGetValue(self, out Info? info))
            return;
        WatchEntitySyncRegistry.PublishEvent(level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.BadelineBoost, info.ID), ActivateEvent, []));
    }

    private static void BadelineBoost_Update(
        On.Celeste.BadelineBoost.orig_Update orig,
        BadelineBoost self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;

        // Preserve vanilla particles, light/bloom visibility and component
        // clocks, but force the Update branch that cannot query Player or call
        // Skip. Authoritative state restores the real travelling value.
        bool travelling = self.travelling;
        self.travelling = true;
        self.holding = null;
        try
        {
            orig(self);
        }
        finally
        {
            self.travelling = travelling;
            self.holding = null;
            self.Collidable = false;
        }
    }
}

internal sealed class WatchFlingBirdAdapter : IWatchEntityAdapter
{
    private const byte ActivateEvent = 1;
    private const int PayloadSize = 20;
    private sealed class Info
    {
        public string Level { get; }
        public int ID { get; }
        public Info(string level, int id) { Level = level; ID = id; }
    }

    private static readonly WatchFlingBirdAdapter instance = new();
    private static readonly ConditionalWeakTable<FlingBird, Info> infos = new();
    public WatchEntityKind Kind => WatchEntityKind.FlingBird;

    public static void Load()
    {
        On.Celeste.FlingBird.ctor_EntityData_Vector2 += FlingBird_ctor;
        On.Celeste.FlingBird.OnPlayer += FlingBird_OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FlingBird.OnPlayer -= FlingBird_OnPlayer;
        On.Celeste.FlingBird.ctor_EntityData_Vector2 -= FlingBird_ctor;
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (FlingBird bird in level.Entities.OfType<FlingBird>().ToArray())
        {
            if (!infos.TryGetValue(bird, out Info? info) || info.Level != level.Session.Level)
                continue;
            byte[] payload = new byte[PayloadSize];
            payload[0] = (byte)bird.state;
            if (bird.Visible) payload[1] |= 1;
            if (bird.Collidable) payload[1] |= 2;
            if (bird.LightningRemoved) payload[1] |= 4;
            WatchEntityPayloadCodec.WriteUInt16(payload, 2,
                checked((ushort)Math.Clamp(bird.segmentIndex, 0, ushort.MaxValue)));
            WatchEntityPayloadCodec.WriteSingle(payload, 4, bird.Position.X);
            WatchEntityPayloadCodec.WriteSingle(payload, 8, bird.Position.Y);
            WatchEntityPayloadCodec.WriteSingle(payload, 12, bird.flingSpeed.X);
            WatchEntityPayloadCodec.WriteSingle(payload, 16, bird.flingSpeed.Y);
            yield return new(new WatchEntityKey(Kind, info.ID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (FlingBird bird in level.Entities.OfType<FlingBird>().ToArray())
        {
            if (!infos.TryGetValue(bird, out Info? info) || info.Level != level.Session.Level)
                continue;
            if (!desired.TryGetValue(info.ID, out WatchEntityState state))
            {
                if (isCompleteState)
                {
                    bird.RemoveSelf();
                    changed = true;
                }
                continue;
            }
            ReadOnlySpan<byte> payload = state.Payload.Span;
            bird.state = (FlingBird.States)payload[0];
            bird.Visible = (payload[1] & 1) != 0;
            bird.Collidable = (payload[1] & 2) != 0;
            bird.LightningRemoved = (payload[1] & 4) != 0;
            bird.segmentIndex = WatchEntityPayloadCodec.ReadUInt16(payload, 2);
            bird.Position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 4),
                WatchEntityPayloadCodec.ReadSingle(payload, 8));
            bird.flingSpeed = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 12),
                WatchEntityPayloadCodec.ReadSingle(payload, 16));
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ActivateEvent || entityEvent.Payload.Length != 0)
            return;
        FlingBird? bird = level.Entities.OfType<FlingBird>().FirstOrDefault(candidate =>
            infos.TryGetValue(candidate, out Info? info)
            && info.Level == level.Session.Level && info.ID == entityEvent.Key.EntityID);
        if (bird is null)
            return;
        Audio.Play("event:/new_content/game/10_farewell/bird_throw", bird.Position);
        bird.sprite.Play("hoverStressed");
        bird.state = FlingBird.States.Fling;
        bird.Collidable = false;
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.FlingBird && state.Key.SubID == 0
            && payload.Length == PayloadSize && payload[0] <= 4
            && (payload[1] & ~0b0000_0111) == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 4))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 12))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 16));
    }

    private static void FlingBird_ctor(
        On.Celeste.FlingBird.orig_ctor_EntityData_Vector2 orig,
        FlingBird self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new Info(data.Level.Name, data.ID));
    }

    private static void FlingBird_OnPlayer(
        On.Celeste.FlingBird.orig_OnPlayer orig,
        FlingBird self,
        Player player
    )
    {
        FlingBird.States previous = self.state;
        orig(self, player);
        if (previous != FlingBird.States.Wait
            || WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level || !infos.TryGetValue(self, out Info? info))
            return;
        WatchEntitySyncRegistry.PublishEvent(level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.FlingBird, info.ID), ActivateEvent, []));
    }
}

internal sealed class WatchWallBoosterAdapter : IWatchEntityAdapter
{
    private static readonly WatchWallBoosterAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.WallBooster;

    public static void Load()
    {
        On.Celeste.WallBooster.ctor_EntityData_Vector2 += WallBooster_ctor;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.WallBooster.ctor_EntityData_Vector2 -= WallBooster_ctor;
        WatchEntityIDTable<WallBooster>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (WallBooster booster in level.Entities.OfType<WallBooster>())
        {
            if (WatchEntityIDTable<WallBooster>.TryGet(booster, level.Session.Level, out int id))
                yield return new(new WatchEntityKey(Kind, id),
                    [booster.IceMode ? (byte)1 : (byte)0, booster.Visible ? (byte)1 : (byte)0]);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, (bool Ice, bool Visible)> desired = new();
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.Kind != Kind || state.Key.SubID != 0 || payload.Length != 2
                || payload[0] > 1 || payload[1] > 1
                || !desired.TryAdd(state.Key.EntityID, (payload[0] != 0, payload[1] != 0)))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (WallBooster booster in level.Entities.OfType<WallBooster>())
        {
            if (!WatchEntityIDTable<WallBooster>.TryGet(booster, level.Session.Level, out int id)
                || !desired.TryGetValue(id, out var state))
                continue;
            if (booster.IceMode != state.Ice)
                booster.OnChangeMode(state.Ice ? Session.CoreModes.Cold : Session.CoreModes.Hot);
            booster.Visible = state.Visible;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }

    private static void WallBooster_ctor(
        On.Celeste.WallBooster.orig_ctor_EntityData_Vector2 orig,
        WallBooster self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<WallBooster>.Set(self, data.Level.Name, data.ID);
    }
}
