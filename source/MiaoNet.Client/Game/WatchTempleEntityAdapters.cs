using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchTorchAdapter : IWatchEntityAdapter
{
    private const byte LightEvent = 1;
    private static readonly WatchTorchAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.Torch;

    public static void Load()
    {
        On.Celeste.Torch.ctor_EntityData_Vector2_EntityID += Torch_ctor;
        On.Celeste.Torch.OnPlayer += Torch_OnPlayer;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Torch.OnPlayer -= Torch_OnPlayer;
        On.Celeste.Torch.ctor_EntityData_Vector2_EntityID -= Torch_ctor;
        WatchEntityIDTable<Torch>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (Torch torch in WatchRoomEntityIndex.Enumerate<Torch>(level))
        {
            if (WatchEntityIDTable<Torch>.TryGet(torch, level.Session.Level, out int id))
                yield return WatchEntityState.FromTyped(
                    new(Kind, id),
                    torch.lit,
                    static value => [value ? (byte)1 : (byte)0]
                );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, bool> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind || state.Key.SubID != 0 || state.Payload.Length != 1
                || state.Payload.Span[0] > 1 || !desired.TryAdd(state.Key.EntityID, state.Payload.Span[0] != 0))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (Torch torch in WatchRoomEntityIndex.Enumerate<Torch>(level))
        {
            if (!WatchEntityIDTable<Torch>.TryGet(torch, level.Session.Level, out int id)
                || !desired.TryGetValue(id, out bool lit) || torch.lit == lit)
                continue;
            if (lit)
                torch.OnPlayer(null!);
            else
            {
                torch.lit = false;
                torch.Collidable = true;
                torch.light.Visible = torch.startLit;
                torch.bloom.Visible = torch.startLit;
            }
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != LightEvent || entityEvent.Payload.Length != 0)
            return;
        Torch? torch = WatchEntityIDTable<Torch>.Find(level, entityEvent.Key.EntityID);
        if (torch is not null && !torch.lit)
            torch.OnPlayer(null!);
    }

    private static void Torch_ctor(
        On.Celeste.Torch.orig_ctor_EntityData_Vector2_EntityID orig,
        Torch self,
        EntityData data,
        Vector2 offset,
        EntityID id
    )
    {
        orig(self, data, offset, id);
        WatchEntityIDTable<Torch>.Set(self, id.Level, id.ID);
    }

    private static void Torch_OnPlayer(On.Celeste.Torch.orig_OnPlayer orig, Torch self, Player player)
    {
        bool wasLit = self.lit;
        orig(self, player);
        if (wasLit || !self.lit || WatchEntitySyncRegistry.IsApplyingRemoteState
            || self.Scene is not Level level
            || !WatchEntityIDTable<Torch>.TryGet(self, level.Session.Level, out int id))
            return;
        WatchEntitySyncRegistry.PublishEvent(level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Torch, id), LightEvent, []));
    }
}

internal sealed class WatchTempleCrackedBlockAdapter : IWatchEntityAdapter
{
    private const byte BreakEvent = 1;
    private static readonly WatchTempleCrackedBlockAdapter instance = new();
    private static readonly Dictionary<(string Level, int ID), bool> broken = new();
    public WatchEntityKind Kind => WatchEntityKind.TempleCrackedBlock;

    public static void Load()
    {
        On.Celeste.TempleCrackedBlock.ctor_EntityID_EntityData_Vector2 += TempleCrackedBlock_ctor;
        On.Celeste.TempleCrackedBlock.Break += TempleCrackedBlock_Break;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.TempleCrackedBlock.Break -= TempleCrackedBlock_Break;
        On.Celeste.TempleCrackedBlock.ctor_EntityID_EntityData_Vector2 -= TempleCrackedBlock_ctor;
        broken.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> live = new();
        foreach (TempleCrackedBlock block in WatchRoomEntityIndex.Enumerate<TempleCrackedBlock>(level))
        {
            if (block.eid.Level != room)
                continue;
            live.Add(block.eid.ID);
            broken[(room, block.eid.ID)] = block.broken;
            yield return Encode(block.eid.ID, block.broken);
        }
        foreach (((string levelName, int id), bool isBroken) in broken
            .Where(pair => pair.Key.Level == room && !live.Contains(pair.Key.ID))
            .OrderBy(pair => pair.Key.ID))
        {
            _ = levelName;
            yield return Encode(id, isBroken);
        }
        foreach (int id in level.Session.LevelData.Entities
            .Where(data => data.Name == "templeCrackedBlock"
                && level.Session.DoNotLoad.Contains(new EntityID(room, data.ID)))
            .Select(data => data.ID)
            .Where(id => !live.Contains(id) && !broken.ContainsKey((room, id)))
            .Order())
        {
            broken[(room, id)] = true;
            yield return Encode(id, true);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, bool> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind || state.Key.SubID != 0 || state.Payload.Length != 1
                || state.Payload.Span[0] > 1 || !desired.TryAdd(state.Key.EntityID, state.Payload.Span[0] != 0))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (TempleCrackedBlock block in WatchRoomEntityIndex.Enumerate<TempleCrackedBlock>(level).ToArray())
        {
            if (block.eid.Level != level.Session.Level
                || !desired.Remove(block.eid.ID, out bool shouldBeBroken))
                continue;
            if (shouldBeBroken)
            {
                if (!block.broken)
                {
                    block.Visible = false;
                    block.Collidable = false;
                    changed = true;
                }
            }
            else if (block.broken || !block.Visible || !block.Collidable)
            {
                block.broken = false;
                block.frame = 0f;
                block.Visible = true;
                block.Collidable = true;
                changed = true;
            }
        }

        if (isCompleteState && WatchEntitySyncRegistry.IsApplyingLifecycleReset)
        {
            int recreated = 0;
            foreach (int id in desired
                .Where(pair => !pair.Value)
                .Select(pair => pair.Key)
                .ToArray())
            {
                if (!TryRecreate(level, id))
                    continue;
                desired.Remove(id);
                recreated++;
            }
            if (recreated > 0)
            {
                level.Entities.UpdateLists();
                changed = true;
                Logger.Debug(
                    LT.MiaoNetWatch,
                    $"Recreated {recreated} TempleCrackedBlock instance(s) for the watched death reset."
                );
            }
        }

        bool reload = desired.Values.Any(isBroken => !isBroken);
        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (reload)
            result |= WatchEntityApplyResult.SceneChanged | WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != BreakEvent || entityEvent.Payload.Length != 8)
            return;
        TempleCrackedBlock? block = WatchRoomEntityIndex.Enumerate<TempleCrackedBlock>(level).FirstOrDefault(candidate =>
            candidate.eid.Level == level.Session.Level && candidate.eid.ID == entityEvent.Key.EntityID);
        if (block is null || block.broken)
            return;
        Vector2 from = new(
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0),
            WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 4));
        block.Visible = true;
        block.Collidable = true;
        block.Break(from);
    }

    private static WatchEntityState Encode(int id, bool isBroken)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.TempleCrackedBlock, id),
            isBroken,
            static value => [value ? (byte)1 : (byte)0]
        );

    private static bool TryRecreate(Level level, int id)
    {
        LevelData levelData = level.Session.LevelData;
        EntityData? data = levelData.Entities.FirstOrDefault(candidate =>
            candidate.ID == id && candidate.Name == "templeCrackedBlock"
        );
        if (data is null)
            return false;

        EntityID entityID = new(level.Session.Level, id);
        level.Session.DoNotLoad.Remove(entityID);
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        level.Add(new TempleCrackedBlock(entityID, data, offset));
        return true;
    }

    private static void TempleCrackedBlock_ctor(
        On.Celeste.TempleCrackedBlock.orig_ctor_EntityID_EntityData_Vector2 orig,
        TempleCrackedBlock self,
        EntityID id,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, id, data, offset);
        broken[(id.Level, id.ID)] = false;
    }

    private static void TempleCrackedBlock_Break(
        On.Celeste.TempleCrackedBlock.orig_Break orig,
        TempleCrackedBlock self,
        Vector2 from
    )
    {
        Level? level = self.Scene as Level;
        EntityID id = self.eid;
        bool publish = level is not null && !self.broken && !WatchEntitySyncRegistry.IsApplyingRemoteState;
        orig(self, from);
        broken[(id.Level, id.ID)] = true;
        if (!publish)
            return;
        byte[] payload = new byte[8];
        WatchEntityPayloadCodec.WriteVector2(payload, 0, from);
        WatchEntitySyncRegistry.PublishEvent(level!,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.TempleCrackedBlock, id.ID), BreakEvent, payload));
    }
}

internal sealed class WatchTempleBigEyeballAdapter : IWatchEntityAdapter
{
    private const byte BounceEvent = 1;
    private const byte BurstEvent = 2;
    private sealed class Info
    {
        public string Level { get; }
        public int ID { get; }
        public Info(string level, int id) { Level = level; ID = id; }
    }

    private static readonly WatchTempleBigEyeballAdapter instance = new();
    private static readonly ConditionalWeakTable<TempleBigEyeball, Info> infos = new();
    public WatchEntityKind Kind => WatchEntityKind.TempleBigEyeball;

    public static void Load()
    {
        On.Celeste.TempleBigEyeball.ctor += TempleBigEyeball_ctor;
        On.Celeste.TempleBigEyeball.OnPlayer += TempleBigEyeball_OnPlayer;
        On.Celeste.TempleBigEyeball.OnHoldable += TempleBigEyeball_OnHoldable;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.TempleBigEyeball.OnHoldable -= TempleBigEyeball_OnHoldable;
        On.Celeste.TempleBigEyeball.OnPlayer -= TempleBigEyeball_OnPlayer;
        On.Celeste.TempleBigEyeball.ctor -= TempleBigEyeball_ctor;
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (TempleBigEyeball eye in WatchRoomEntityIndex.Enumerate<TempleBigEyeball>(level))
        {
            if (infos.TryGetValue(eye, out Info? info) && info.Level == level.Session.Level)
                yield return WatchEntityState.FromTyped(
                    new(Kind, info.ID),
                    (eye.triggered, eye.bursting),
                    static state =>
                    [
                        state.triggered ? (byte)1 : (byte)0,
                        state.bursting ? (byte)1 : (byte)0,
                    ]
                );
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool isCompleteState)
    {
        Dictionary<int, (bool Triggered, bool Bursting)> desired = new();
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> payload = state.Payload.Span;
            if (state.Key.Kind != Kind || state.Key.SubID != 0 || payload.Length != 2
                || payload[0] > 1 || payload[1] > 1
                || !desired.TryAdd(state.Key.EntityID, (payload[0] != 0, payload[1] != 0)))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        foreach (TempleBigEyeball eye in WatchRoomEntityIndex.Enumerate<TempleBigEyeball>(level))
        {
            if (!infos.TryGetValue(eye, out Info? info) || info.Level != level.Session.Level
                || !desired.TryGetValue(info.ID, out var state))
                continue;
            if (state.Triggered && !eye.triggered)
                ApplyBurstPresentation(level, eye);
            eye.triggered = state.Triggered;
            eye.bursting = state.Bursting;
            eye.Collidable = !state.Triggered;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        TempleBigEyeball? eye = WatchRoomEntityIndex.Enumerate<TempleBigEyeball>(level).FirstOrDefault(candidate =>
            infos.TryGetValue(candidate, out Info? info)
            && info.Level == level.Session.Level && info.ID == entityEvent.Key.EntityID);
        if (eye is null || entityEvent.Payload.Length != 0)
            return;
        if (entityEvent.EventID == BounceEvent)
        {
            Audio.Play("event:/game/05_mirror_temple/eyewall_bounce", eye.Position);
            eye.bounceWiggler.Start();
        }
        else if (entityEvent.EventID == BurstEvent && !eye.triggered)
        {
            ApplyBurstPresentation(level, eye);
        }
    }

    private static void ApplyBurstPresentation(Level level, TempleBigEyeball eye)
    {
        eye.triggered = true;
        eye.bursting = true;
        eye.Collidable = false;
        eye.bounceWiggler.Start();
        Audio.Play("event:/game/05_mirror_temple/eyewall_destroy", eye.Position);
        eye.sprite.Play("burst");
        eye.pupil.Visible = false;
        level.Shake();
        foreach (TempleEye templeEye in WatchRoomEntityIndex.Enumerate<TempleEye>(level))
            templeEye.Burst();
    }

    private static void Publish(TempleBigEyeball self, byte eventID)
    {
        if (self.Scene is Level level && infos.TryGetValue(self, out Info? info))
            WatchEntitySyncRegistry.PublishEvent(level,
                new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.TempleBigEyeball, info.ID), eventID, []));
    }

    private static void TempleBigEyeball_ctor(
        On.Celeste.TempleBigEyeball.orig_ctor orig,
        TempleBigEyeball self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        infos.AddOrUpdate(self, new Info(data.Level.Name, data.ID));
    }

    private static void TempleBigEyeball_OnPlayer(
        On.Celeste.TempleBigEyeball.orig_OnPlayer orig,
        TempleBigEyeball self,
        Player player
    )
    {
        bool triggered = self.triggered;
        orig(self, player);
        if (!triggered && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, BounceEvent);
    }

    private static void TempleBigEyeball_OnHoldable(
        On.Celeste.TempleBigEyeball.orig_OnHoldable orig,
        TempleBigEyeball self,
        Holdable holdable
    )
    {
        if (holdable.Entity is TheoCrystal crystal
            && WatchTheoCrystalAdapter.IsSpectatorReplica(crystal))
            return;

        bool triggered = self.triggered;
        orig(self, holdable);
        if (!triggered && self.triggered && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, BurstEvent);
    }
}
