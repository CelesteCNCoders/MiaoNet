using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRumbleTriggerAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 16;
    private const byte InvokeEvent = 1;
    private static readonly WatchRumbleTriggerAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.RumbleTrigger;

    public static void Load()
    {
        On.Celeste.RumbleTrigger.OnEnter += RumbleTrigger_OnEnter;
        On.Celeste.RumbleTrigger.Invoke += RumbleTrigger_Invoke;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.RumbleTrigger.Invoke -= RumbleTrigger_Invoke;
        On.Celeste.RumbleTrigger.OnEnter -= RumbleTrigger_OnEnter;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (RumbleTrigger trigger in level.Entities.OfType<RumbleTrigger>())
        {
            byte[] payload = new byte[PayloadSize];
            if (trigger.started) payload[0] |= 1;
            if (trigger.persistent) payload[0] |= 2;
            if (trigger.manualTrigger) payload[0] |= 4;
            WatchEntityPayloadCodec.WriteSingle(payload, 4, trigger.rumble);
            WatchEntityPayloadCodec.WriteSingle(payload, 8, trigger.left);
            WatchEntityPayloadCodec.WriteSingle(payload, 12, trigger.right);
            yield return new(new(Kind, trigger.id.ID), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityState> desired = states.ToDictionary(state => state.Key.EntityID);
        bool changed = false;
        foreach (RumbleTrigger trigger in level.Entities.OfType<RumbleTrigger>())
        {
            if (!desired.Remove(trigger.id.ID, out WatchEntityState state)
                || !TryDecode(state, out byte flags, out float rumble, out float left, out float right))
                continue;
            trigger.started = (flags & 1) != 0;
            trigger.persistent = (flags & 2) != 0;
            trigger.manualTrigger = (flags & 4) != 0;
            trigger.rumble = rumble;
            trigger.left = left;
            trigger.right = right;
            trigger.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != InvokeEvent || entityEvent.Payload.Length != 4)
            return;
        RumbleTrigger? trigger = level.Entities.OfType<RumbleTrigger>()
            .FirstOrDefault(candidate => candidate.id.ID == entityEvent.Key.EntityID);
        trigger?.Invoke(WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0));
    }

    private static bool TryDecode(
        WatchEntityState state,
        out byte flags,
        out float rumble,
        out float left,
        out float right
    )
    {
        flags = 0;
        rumble = left = right = 0f;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.RumbleTrigger || state.Key.SubID != 0
            || payload.Length != PayloadSize || (payload[0] & ~0b111) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        flags = payload[0];
        rumble = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        left = WatchEntityPayloadCodec.ReadSingle(payload, 8);
        right = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        return float.IsFinite(rumble) && float.IsFinite(left) && float.IsFinite(right);
    }

    private static void RumbleTrigger_OnEnter(
        On.Celeste.RumbleTrigger.orig_OnEnter orig,
        RumbleTrigger self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void RumbleTrigger_Invoke(
        On.Celeste.RumbleTrigger.orig_Invoke orig,
        RumbleTrigger self,
        float delay
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        if (!MiaoNetModule.IsWatching && self.Scene is Level level)
        {
            byte[] payload = new byte[4];
            WatchEntityPayloadCodec.WriteSingle(payload, 0, delay);
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new(
                    new(WatchEntityKind.RumbleTrigger, self.id.ID),
                    InvokeEvent,
                    payload
                )
            );
        }
        orig(self, delay);
    }
}

internal sealed class WatchRumbleWallAdapter : IWatchEntityAdapter
{
    private const byte BreakEvent = 1;
    private static readonly WatchRumbleWallAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.RumbleWall;

    public static void Load()
    {
        On.Celeste.CrumbleWallOnRumble.Break += CrumbleWallOnRumble_Break;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.CrumbleWallOnRumble.Break -= CrumbleWallOnRumble_Break;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (CrumbleWallOnRumble wall in level.Entities.OfType<CrumbleWallOnRumble>())
            yield return new(new(Kind, wall.id.ID), []);
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        if (states.Any(state => state.Key.Kind != Kind || state.Key.SubID != 0
            || state.Payload.Length != 0))
            return WatchEntityApplyResult.None;
        HashSet<int> desired = states.Select(state => state.Key.EntityID).ToHashSet();
        string room = level.Session.Level;
        Dictionary<int, CrumbleWallOnRumble> existing = level.Entities
            .OfType<CrumbleWallOnRumble>()
            .ToDictionary(wall => wall.id.ID);
        bool changed = false;
        foreach (int id in desired)
        {
            if (existing.Remove(id))
                continue;
            EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
                entity.ID == id && entity.Name == "crumbleWallOnRumble"
            );
            if (data is null)
                continue;
            level.Add(new CrumbleWallOnRumble(
                data,
                new(level.Bounds.Left, level.Bounds.Top),
                new(room, id)
            ));
            changed = true;
        }
        if (isCompleteState)
        {
            foreach (CrumbleWallOnRumble wall in existing.Values)
            {
                wall.RemoveSelf();
                changed = true;
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != BreakEvent || entityEvent.Payload.Length != 0)
            return;
        level.Entities.OfType<CrumbleWallOnRumble>()
            .FirstOrDefault(wall => wall.id.ID == entityEvent.Key.EntityID)?.Break();
    }

    private static void CrumbleWallOnRumble_Break(
        On.Celeste.CrumbleWallOnRumble.orig_Break orig,
        CrumbleWallOnRumble self
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        if (!MiaoNetModule.IsWatching && self.Scene is Level level)
        {
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new(new(WatchEntityKind.RumbleWall, self.id.ID), BreakEvent, [])
            );
        }
        orig(self);
    }
}

internal sealed class WatchBridgeAdapter : IWatchEntityAdapter
{
    private const int ControllerPayloadSize = 16;
    private const int TilePayloadSize = 32;
    private const byte FallEvent = 1;

    private sealed class BridgeInfo
    {
        public string Level { get; }
        public int ID { get; }
        public List<BridgeTile> Tiles { get; } = [];

        public BridgeInfo(string level, int id)
        {
            Level = level;
            ID = id;
        }
    }

    private sealed class TileInfo
    {
        public BridgeInfo Owner { get; }
        public ushort SubID { get; }

        public TileInfo(BridgeInfo owner, ushort subID)
        {
            Owner = owner;
            SubID = subID;
        }
    }

    private static readonly WatchBridgeAdapter instance = new();
    private static readonly ConditionalWeakTable<Bridge, BridgeInfo> bridges = new();
    private static readonly ConditionalWeakTable<BridgeTile, TileInfo> tiles = new();
    private static readonly ConditionalWeakTable<Bridge, WatchTimedStateCache> controllerSync = new();
    private static readonly ConditionalWeakTable<BridgeTile, WatchTimedStateCache> tileSync = new();
    private static readonly ConditionalWeakTable<BridgeTile, WatchRemotePosition> remoteTiles = new();

    public WatchEntityKind Kind => WatchEntityKind.Bridge;

    public static void Load()
    {
        On.Celeste.Bridge.ctor_EntityData_Vector2 += Bridge_ctor;
        On.Celeste.Bridge.Added += Bridge_Added;
        On.Celeste.Bridge.Update += Bridge_Update;
        On.Celeste.BridgeTile.Update += BridgeTile_Update;
        On.Celeste.BridgeTile.Fall += BridgeTile_Fall;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BridgeTile.Fall -= BridgeTile_Fall;
        On.Celeste.BridgeTile.Update -= BridgeTile_Update;
        On.Celeste.Bridge.Update -= Bridge_Update;
        On.Celeste.Bridge.Added -= Bridge_Added;
        On.Celeste.Bridge.ctor_EntityData_Vector2 -= Bridge_ctor;
        WatchEntityIDTable<Bridge>.Clear();
        bridges.Clear();
        tiles.Clear();
        controllerSync.Clear();
        tileSync.Clear();
        remoteTiles.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (Bridge bridge in level.Entities.OfType<Bridge>())
        {
            if (!bridges.TryGetValue(bridge, out BridgeInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level))
                continue;
            int id = info.ID;
            byte[] controller = new byte[ControllerPayloadSize];
            if (bridge.canCollapse) controller[0] |= 1;
            if (bridge.ending) controller[0] |= 2;
            if (bridge.canEndCollapseA) controller[0] |= 4;
            if (bridge.canEndCollapseB) controller[0] |= 8;
            WatchEntityPayloadCodec.WriteSingle(controller, 4, bridge.collapseTimer);
            WatchEntityPayloadCodec.WriteSingle(controller, 8, bridge.gapStartX);
            WatchEntityPayloadCodec.WriteSingle(controller, 12, bridge.gapEndX);
            yield return controllerSync.GetValue(bridge, static _ => new()).Capture(
                new(Kind, id), controller, 1, level.TimeActive,
                WatchEntitySyncRegistry.IsCapturingCurrentState
            );
            for (int index = 0; index < info.Tiles.Count; index++)
            {
                BridgeTile tile = info.Tiles[index];
                if (!ReferenceEquals(tile.Scene, level))
                    continue;
                byte[] payload = new byte[TilePayloadSize];
                if (tile.Fallen) payload[0] |= 1;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, tile.Position);
                WatchEntityPayloadCodec.WriteSingle(payload, 12, tile.speedY);
                WatchEntityPayloadCodec.WriteSingle(payload, 16, tile.colorLerp);
                WatchEntityPayloadCodec.WriteVector2(payload, 20, tile.shakeOffset);
                WatchEntityPayloadCodec.WriteSingle(payload, 28, tile.shakeTimer);
                yield return tileSync.GetValue(tile, static _ => new()).Capture(
                    new(Kind, id, checked((ushort)(index + 1))), payload, 1,
                    level.TimeActive, WatchEntitySyncRegistry.IsCapturingCurrentState
                );
            }
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        bool changed = false;
        HashSet<(int ID, ushort SubID)> presentTiles = states
            .Where(state => state.Key.SubID > 0)
            .Select(state => (state.Key.EntityID, state.Key.SubID))
            .ToHashSet();
        foreach (IGrouping<int, WatchEntityState> group in states.GroupBy(state => state.Key.EntityID))
        {
            Bridge? bridge = level.Entities.OfType<Bridge>().FirstOrDefault(candidate =>
                bridges.TryGetValue(candidate, out BridgeInfo? info)
                && StringComparer.Ordinal.Equals(info.Level, level.Session.Level)
                && info.ID == group.Key
            );
            if (bridge is null || !bridges.TryGetValue(bridge, out BridgeInfo? bridgeInfo))
                continue;
            foreach (WatchEntityState state in group)
            {
                ReadOnlySpan<byte> payload = state.Payload.Span;
                if (state.Key.SubID == 0 && payload.Length == ControllerPayloadSize)
                {
                    bridge.canCollapse = (payload[0] & 1) != 0;
                    bridge.ending = (payload[0] & 2) != 0;
                    bridge.canEndCollapseA = (payload[0] & 4) != 0;
                    bridge.canEndCollapseB = (payload[0] & 8) != 0;
                    bridge.collapseTimer = WatchEntityPayloadCodec.ReadSingle(payload, 4);
                    bridge.gapStartX = WatchEntityPayloadCodec.ReadSingle(payload, 8);
                    bridge.gapEndX = WatchEntityPayloadCodec.ReadSingle(payload, 12);
                    changed = true;
                }
                else if (state.Key.SubID > 0 && state.Key.SubID <= bridgeInfo.Tiles.Count
                    && payload.Length == TilePayloadSize)
                {
                    BridgeTile tile = bridgeInfo.Tiles[state.Key.SubID - 1];
                    bool fallen = (payload[0] & 1) != 0;
                    float shakeTimer = WatchEntityPayloadCodec.ReadSingle(payload, 28);
                    if (!tile.Fallen && fallen)
                        tile.Fall(Math.Max(0f, shakeTimer));
                    else
                        tile.Fallen = fallen;
                    remoteTiles.GetValue(tile, static _ => new()).Apply(tile, WatchEntityPayloadCodec.ReadVector2(payload, 4));
                    tile.speedY = WatchEntityPayloadCodec.ReadSingle(payload, 12);
                    tile.colorLerp = WatchEntityPayloadCodec.ReadSingle(payload, 16);
                    tile.shakeOffset = WatchEntityPayloadCodec.ReadVector2(payload, 20);
                    tile.shakeTimer = shakeTimer;
                    changed = true;
                }
            }
        }

        if (isCompleteState)
        {
            foreach (Bridge bridge in level.Entities.OfType<Bridge>())
            {
                if (!bridges.TryGetValue(bridge, out BridgeInfo? info)
                    || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level))
                    continue;
                for (int index = 0; index < info.Tiles.Count; index++)
                {
                    BridgeTile tile = info.Tiles[index];
                    ushort subID = checked((ushort)(index + 1));
                    if (ReferenceEquals(tile.Scene, level)
                        && !presentTiles.Contains((info.ID, subID)))
                    {
                        tile.RemoveSelf();
                        changed = true;
                    }
                }
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != FallEvent || entityEvent.Payload.Length != 4
            || entityEvent.Key.SubID == 0)
            return;
        float delay = WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0);
        if (!float.IsFinite(delay) || delay < 0f || delay > 1f)
            return;
        BridgeTile? tile = FindTile(level, entityEvent.Key);
        if (tile is not null && !tile.Fallen)
            tile.Fall(delay);
    }

    private static void Bridge_ctor(
        On.Celeste.Bridge.orig_ctor_EntityData_Vector2 orig,
        Bridge self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<Bridge>.Set(self, data.Level.Name, data.ID);
        bridges.AddOrUpdate(self, new BridgeInfo(data.Level.Name, data.ID));
    }

    private static void Bridge_Added(
        On.Celeste.Bridge.orig_Added orig,
        Bridge self,
        Scene scene
    )
    {
        orig(self, scene);
        if (!bridges.TryGetValue(self, out BridgeInfo? info))
            return;
        info.Tiles.Clear();
        info.Tiles.AddRange(self.tiles);
        for (int index = 0; index < info.Tiles.Count; index++)
            tiles.AddOrUpdate(
                info.Tiles[index],
                new TileInfo(info, checked((ushort)(index + 1)))
            );
    }

    private static void Bridge_Update(On.Celeste.Bridge.orig_Update orig, Bridge self)
    {
        if (!MiaoNetModule.IsWatching)
            orig(self);
        else if (!MiaoNetModule.IsWatchedPlayerPaused)
            self.Components.Update();
    }

    private static void BridgeTile_Update(
        On.Celeste.BridgeTile.orig_Update orig,
        BridgeTile self
    )
    {
        orig(self);
        if (MiaoNetModule.IsWatching && !MiaoNetModule.IsWatchedPlayerPaused)
            remoteTiles.GetValue(self, static _ => new()).Update(self);
    }

    private static void BridgeTile_Fall(
        On.Celeste.BridgeTile.orig_Fall orig,
        BridgeTile self,
        float delay
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        if (!MiaoNetModule.IsWatching && self.Scene is Level level
            && tiles.TryGetValue(self, out TileInfo? info)
            && StringComparer.Ordinal.Equals(info.Owner.Level, level.Session.Level))
        {
            byte[] payload = new byte[4];
            WatchEntityPayloadCodec.WriteSingle(payload, 0, delay);
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.Bridge, info.Owner.ID, info.SubID),
                FallEvent,
                payload
            ));
        }
        orig(self, delay);
    }

    private static BridgeTile? FindTile(Level level, WatchEntityKey key)
    {
        foreach (Bridge bridge in level.Entities.OfType<Bridge>())
        {
            if (!bridges.TryGetValue(bridge, out BridgeInfo? info)
                || info.ID != key.EntityID
                || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level)
                || key.SubID == 0
                || key.SubID > info.Tiles.Count)
                continue;
            return info.Tiles[key.SubID - 1];
        }
        return null;
    }
}

internal sealed class WatchIntroCrusherAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 28;
    private static readonly WatchIntroCrusherAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.IntroCrusher;

    public static void Load()
    {
        On.Celeste.IntroCrusher.ctor_EntityData_Vector2 += IntroCrusher_ctor;
        On.Celeste.IntroCrusher.Update += IntroCrusher_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.IntroCrusher.Update -= IntroCrusher_Update;
        On.Celeste.IntroCrusher.ctor_EntityData_Vector2 -= IntroCrusher_ctor;
        WatchEntityIDTable<IntroCrusher>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (IntroCrusher crusher in level.Entities.OfType<IntroCrusher>())
        {
            if (!WatchEntityIDTable<IntroCrusher>.TryGet(crusher, level.Session.Level, out int id))
                continue;
            byte[] payload = new byte[PayloadSize];
            if (crusher.Visible) payload[0] |= 1;
            if (crusher.Collidable) payload[0] |= 2;
            if (crusher.Active) payload[0] |= 4;
            WatchEntityPayloadCodec.WriteVector2(payload, 4, crusher.Position);
            WatchEntityPayloadCodec.WriteVector2(payload, 12, crusher.shake);
            WatchEntityPayloadCodec.WriteVector2(payload, 20, crusher.start);
            yield return new(new(Kind, id), payload);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.SubID != 0 || payload.Length != PayloadSize)
                continue;
            IntroCrusher? crusher = WatchEntityIDTable<IntroCrusher>.Find(level, state.Key.EntityID);
            if (crusher is null)
                continue;
            Vector2 target = WatchEntityPayloadCodec.ReadVector2(payload, 4);
            Vector2 movement = target - crusher.Position;
            crusher.Position = target;
            if (movement != Vector2.Zero)
                crusher.MoveStaticMovers(movement);
            crusher.shake = WatchEntityPayloadCodec.ReadVector2(payload, 12);
            crusher.Visible = (payload[0] & 1) != 0;
            crusher.Collidable = false;
            crusher.Active = (payload[0] & 4) != 0;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void IntroCrusher_ctor(
        On.Celeste.IntroCrusher.orig_ctor_EntityData_Vector2 orig,
        IntroCrusher self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<IntroCrusher>.Set(self, data.Level.Name, data.ID);
    }

    private static void IntroCrusher_Update(
        On.Celeste.IntroCrusher.orig_Update orig,
        IntroCrusher self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        foreach (Coroutine coroutine in self.Components.GetAll<Coroutine>())
            coroutine.Active = false;
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            self.Components.Update();
        self.Collidable = false;
    }
}

internal sealed class WatchResortRoofEndingAdapter : IWatchEntityAdapter
{
    private const int ImagePayloadSize = 28;
    private static readonly WatchResortRoofEndingAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.ResortRoofEnding;

    public static void Load()
    {
        On.Celeste.ResortRoofEnding.ctor += ResortRoofEnding_ctor;
        On.Celeste.ResortRoofEnding.Wobble += ResortRoofEnding_Wobble;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ResortRoofEnding.Wobble -= ResortRoofEnding_Wobble;
        On.Celeste.ResortRoofEnding.ctor -= ResortRoofEnding_ctor;
        WatchEntityIDTable<ResortRoofEnding>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (ResortRoofEnding roof in level.Entities.OfType<ResortRoofEnding>())
        {
            if (!WatchEntityIDTable<ResortRoofEnding>.TryGet(roof, level.Session.Level, out int id))
                continue;
            yield return new(new(Kind, id), new byte[] { roof.BeginFalling ? (byte)1 : (byte)0 });
            for (int index = 0; index < roof.images.Count; index++)
            {
                Image image = roof.images[index];
                byte[] payload = new byte[ImagePayloadSize];
                if (image.Visible) payload[0] |= 1;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, image.Position);
                WatchEntityPayloadCodec.WriteSingle(payload, 12, image.Rotation);
                WatchEntityPayloadCodec.WriteVector2(payload, 16, image.Scale);
                WatchEntityPayloadCodec.WriteSingle(payload, 24, image.Color.A / 255f);
                yield return new(new(Kind, id, checked((ushort)(index + 1))), payload);
            }
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        bool changed = false;
        foreach (IGrouping<int, WatchEntityState> group in states.GroupBy(state => state.Key.EntityID))
        {
            ResortRoofEnding? roof = WatchEntityIDTable<ResortRoofEnding>.Find(level, group.Key);
            if (roof is null)
                continue;
            foreach (WatchEntityState state in group)
            {
                ReadOnlySpan<byte> payload = state.Payload.Span;
                if (state.Key.SubID == 0 && payload.Length == 1)
                {
                    roof.BeginFalling = payload[0] != 0;
                    changed = true;
                }
                else if (state.Key.SubID > 0 && state.Key.SubID <= roof.images.Count
                    && payload.Length == ImagePayloadSize)
                {
                    Image image = roof.images[state.Key.SubID - 1];
                    image.Visible = (payload[0] & 1) != 0;
                    image.Position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
                    image.Rotation = WatchEntityPayloadCodec.ReadSingle(payload, 12);
                    image.Scale = WatchEntityPayloadCodec.ReadVector2(payload, 16);
                    image.Color = Color.White * Calc.Clamp(
                        WatchEntityPayloadCodec.ReadSingle(payload, 24), 0f, 1f
                    );
                    changed = true;
                }
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void ResortRoofEnding_ctor(
        On.Celeste.ResortRoofEnding.orig_ctor orig,
        ResortRoofEnding self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<ResortRoofEnding>.Set(self, data.Level.Name, data.ID);
    }

    private static void ResortRoofEnding_Wobble(
        On.Celeste.ResortRoofEnding.orig_Wobble orig,
        ResortRoofEnding self,
        AngryOshiro oshiro,
        bool fall
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, oshiro, fall);
    }
}
