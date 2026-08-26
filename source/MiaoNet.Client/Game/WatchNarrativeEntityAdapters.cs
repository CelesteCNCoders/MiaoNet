using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal static class WatchMapEntityMatcher
{
    public static void AssignIDs<TEntity>(Level level, IEnumerable<TEntity> entities, params string[] mapNames)
        where TEntity : Entity
    {
        List<TEntity> missing = entities
            .Where(entity => !WatchEntityIDTable<TEntity>.TryGet(entity, level.Session.Level, out _))
            .ToList();
        if (missing.Count == 0)
            return;

        HashSet<int> used = entities.Select(entity =>
            WatchEntityIDTable<TEntity>.TryGet(entity, level.Session.Level, out int id) ? id : int.MinValue)
            .Where(id => id != int.MinValue).ToHashSet();
        List<EntityData> candidates = level.Session.LevelData.Entities
            .Where(data => mapNames.Contains(data.Name, StringComparer.OrdinalIgnoreCase)
                && !used.Contains(data.ID))
            .ToList();

        foreach (TEntity entity in missing)
        {
            EntityData? closest = candidates.MinBy(data =>
                Vector2.DistanceSquared(entity.Position, data.Position + level.LevelOffset));
            if (closest is null)
                continue;
            WatchEntityIDTable<TEntity>.Set(entity, level.Session.Level, closest.ID);
            candidates.Remove(closest);
        }
    }
}

internal static class WatchSpriteState
{
    public static ushort EncodeAnimation(Sprite sprite)
    {
        string? current = sprite.CurrentAnimationID;
        if (string.IsNullOrEmpty(current))
            return ushort.MaxValue;
        int index = sprite.Animations.Keys.OrderBy(id => id, StringComparer.Ordinal)
            .ToList().FindIndex(id => id == current);
        return index is >= 0 and < ushort.MaxValue ? (ushort)index : ushort.MaxValue;
    }

    public static void ApplyAnimation(Sprite sprite, ushort animation, byte frame)
    {
        if (animation == ushort.MaxValue)
            return;
        string? id = sprite.Animations.Keys.OrderBy(value => value, StringComparer.Ordinal)
            .ElementAtOrDefault(animation);
        if (id is null)
            return;
        bool changed = sprite.CurrentAnimationID != id;
        if (changed)
            sprite.Play(id, restart: true);
        int drift = Math.Abs(sprite.CurrentAnimationFrame - frame);
        if (sprite.CurrentAnimationTotalFrames > 0
            && (changed || WatchEntitySyncRegistry.IsApplyingLifecycleReset || drift > 2))
            sprite.SetAnimationFrame(Math.Min(frame, sprite.CurrentAnimationTotalFrames - 1));
    }
}

/// <summary>
/// A read-only stand-in for a remote narrative NPC which no longer exists in
/// the Watcher's local scene after its cutscene was skipped. It intentionally
/// owns only presentation components and can never start dialogue, collide
/// with the local Player, or mutate Session state.
/// </summary>
internal sealed class WatchNarrativeNpcProxy : Entity
{
    private readonly WatchRemotePosition remote = new();

    public int WatchEntityID { get; }
    public WatchNarrativeNPCVisual VisualKind { get; }
    public Sprite Sprite { get; }
    public VertexLight Light { get; }
    public WatchRemotePosition PositionSync => remote;

    public WatchNarrativeNpcProxy(int entityID, WatchNarrativeNPCVisual visual, Vector2 position)
        : base(position)
    {
        WatchEntityID = entityID;
        VisualKind = visual;
        Sprite = visual switch
        {
            WatchNarrativeNPCVisual.Granny => GFX.SpriteBank.Create("granny"),
            WatchNarrativeNPCVisual.Theo => GFX.SpriteBank.Create("theo"),
            WatchNarrativeNPCVisual.Oshiro => new OshiroSprite(1),
            WatchNarrativeNPCVisual.BadelineBoss => GFX.SpriteBank.Create("badeline_boss"),
            _ => throw new ArgumentOutOfRangeException(nameof(visual), visual, null),
        };
        Light = new VertexLight(Color.White, 1f, 32, 64);
        Add(Sprite);
        Add(Light);
        Collidable = false;
    }

    public override void Update()
    {
        if (!MiaoNetModule.IsWatching)
        {
            RemoveSelf();
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        remote.Update(this);
        base.Update();
    }
}

internal sealed class WatchNarrativeNPCAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 36;
    private static readonly WatchNarrativeNPCAdapter instance = new();
    private static readonly ConditionalWeakTable<NPC, WatchTimedStateCache> sync = new();
    private static readonly ConditionalWeakTable<NPC, WatchRemotePosition> remote = new();

    public WatchEntityKind Kind => WatchEntityKind.NarrativeNPC;

    public static void Load() => WatchEntitySyncRegistry.Register(instance);

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        WatchEntityIDTable<NPC>.Clear();
        sync.Clear();
        remote.Clear();
    }

    internal static void UpdatePresentation(NPC npc)
    {
        if (!MiaoNetModule.IsWatchedPlayerPaused && remote.TryGetValue(npc, out WatchRemotePosition? position))
            position.Update(npc);
    }

    private static void AssignRuntimeIDs(Level level, List<NPC> npcs)
    {
        foreach (IGrouping<Type, NPC> group in npcs
            .Where(npc => !WatchEntityIDTable<NPC>.TryGet(npc, level.Session.Level, out _))
            .GroupBy(npc => npc.GetType()))
        {
            uint hash = 2166136261;
            foreach (char value in group.Key.FullName ?? group.Key.Name)
                hash = (hash ^ value) * 16777619;
            int prefix = 1_000_000 + (int)(hash % 900_000_000u);
            int ordinal = 0;
            foreach (NPC npc in group.OrderBy(entity => entity.Depth).ThenBy(entity => entity.X).ThenBy(entity => entity.Y))
                WatchEntityIDTable<NPC>.Set(npc, level.Session.Level, prefix + ordinal++);
        }
    }

    private static WatchNarrativeNPCVisual GetVisualKind(NPC npc)
    {
        if (npc.Sprite is OshiroSprite)
            return WatchNarrativeNPCVisual.Oshiro;

        string? spriteID = SpriteIDTracker.LookupID(npc.Sprite);
        if (spriteID == "granny")
            return WatchNarrativeNPCVisual.Granny;
        if (spriteID == "theo")
            return WatchNarrativeNPCVisual.Theo;
        if (spriteID == "badeline_boss")
            return WatchNarrativeNPCVisual.BadelineBoss;

        // The tracker is installed before any Level is loaded, but retain a
        // vanilla-type fallback for entities constructed unusually early.
        string typeName = npc.GetType().Name;
        if (typeName.Contains("Granny", StringComparison.Ordinal))
            return WatchNarrativeNPCVisual.Granny;
        if (typeName.Contains("Theo", StringComparison.Ordinal))
            return WatchNarrativeNPCVisual.Theo;
        return WatchNarrativeNPCVisual.Unknown;
    }

    private static void ApplyPresentation(
        Entity entity,
        Sprite sprite,
        VertexLight? light,
        ReadOnlySpan<byte> p,
        WatchRemotePosition? position = null)
    {
        Vector2 target = new(
            WatchEntityPayloadCodec.ReadSingle(p, 8),
            WatchEntityPayloadCodec.ReadSingle(p, 12));
        if (position is null)
            entity.Position = target;
        else
            position.Apply(entity, target);
        entity.Visible = (p[0] & 1) != 0;
        sprite.Visible = (p[0] & 2) != 0;
        if (light is not null)
        {
            light.Visible = (p[0] & 32) != 0 && (p[0] & 4) != 0;
            light.Alpha = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(p, 24), 0f, 1f);
        }
        entity.Collidable = false;
        sprite.Scale = new(
            WatchEntityPayloadCodec.ReadSingle(p, 16),
            WatchEntityPayloadCodec.ReadSingle(p, 20));
        sprite.Rotation = WatchEntityPayloadCodec.ReadSingle(p, 32);
        entity.Depth = BinaryPrimitives.ReadInt32LittleEndian(p[28..]);
        WatchSpriteState.ApplyAnimation(sprite, WatchEntityPayloadCodec.ReadUInt16(p, 2), p[1]);
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        List<NPC> npcs = level.Entities.OfType<NPC>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, npcs, "npc");
        AssignRuntimeIDs(level, npcs);
        foreach (NPC npc in npcs)
        {
            if (!WatchEntityIDTable<NPC>.TryGet(npc, level.Session.Level, out int id)
                || npc.Sprite is null)
                continue;
            byte[] p = new byte[PayloadSize];
            if (npc.Visible) p[0] |= 1;
            if (npc.Sprite.Visible) p[0] |= 2;
            if (npc.Light?.Visible == true) p[0] |= 4;
            if (npc.Collidable) p[0] |= 8;
            if (npc.Active) p[0] |= 16;
            if (npc.Light is not null) p[0] |= 32;
            p[1] = (byte)Math.Max(0, npc.Sprite.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteUInt16(p, 2, WatchSpriteState.EncodeAnimation(npc.Sprite));
            p[4] = (byte)GetVisualKind(npc);
            WatchEntityPayloadCodec.WriteSingle(p, 8, npc.X);
            WatchEntityPayloadCodec.WriteSingle(p, 12, npc.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 16, npc.Sprite.Scale.X);
            WatchEntityPayloadCodec.WriteSingle(p, 20, npc.Sprite.Scale.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 24, npc.Light?.Alpha ?? 0f);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(28), npc.Depth);
            WatchEntityPayloadCodec.WriteSingle(p, 32, npc.Sprite.Rotation);
            yield return sync.GetValue(npc, static _ => new()).Capture(
                new(Kind, id), p, 1, level.TimeActive,
                WatchEntitySyncRegistry.IsCapturingCurrentState);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool complete)
    {
        List<NPC> npcs = level.Entities.OfType<NPC>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, npcs, "npc");
        AssignRuntimeIDs(level, npcs);
        List<WatchNarrativeNpcProxy> proxies = level.Entities.OfType<WatchNarrativeNpcProxy>().ToList();
        HashSet<int> desiredIDs = states.Select(state => state.Key.EntityID).ToHashSet();
        bool changed = false;
        if (complete)
        {
            foreach (NPC npc in npcs)
            {
                if (WatchEntityIDTable<NPC>.TryGet(npc, level.Session.Level, out int id)
                    && !desiredIDs.Contains(id))
                {
                    npc.Visible = false;
                    npc.Active = false;
                    npc.Collidable = false;
                    if (npc.Talker is not null) npc.Talker.Enabled = false;
                    changed = true;
                }
            }
            foreach (WatchNarrativeNpcProxy proxy in proxies.Where(proxy => !desiredIDs.Contains(proxy.WatchEntityID)))
            {
                proxy.RemoveSelf();
                changed = true;
            }
        }
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize)
                continue;
            NPC? npc = npcs.FirstOrDefault(candidate =>
                WatchEntityIDTable<NPC>.TryGet(candidate, level.Session.Level, out int id)
                && id == state.Key.EntityID);
            WatchNarrativeNpcProxy? proxy = proxies.FirstOrDefault(candidate =>
                candidate.WatchEntityID == state.Key.EntityID);
            if (npc?.Sprite is not null)
            {
                if (proxy is not null)
                {
                    proxy.RemoveSelf();
                    proxies.Remove(proxy);
                }
                ApplyPresentation(npc, npc.Sprite, npc.Light, p,
                    remote.GetValue(npc, static _ => new()));
                npc.Active = (p[0] & 16) != 0;
                if (npc.Talker is not null)
                    npc.Talker.Enabled = false;
                changed = true;
                continue;
            }

            WatchNarrativeNPCVisual visual = (WatchNarrativeNPCVisual)p[4];
            if (visual == WatchNarrativeNPCVisual.Unknown)
            {
                proxy?.RemoveSelf();
                if (proxy is not null)
                    proxies.Remove(proxy);
                continue;
            }
            if (proxy is null || proxy.VisualKind != visual)
            {
                proxy?.RemoveSelf();
                if (proxy is not null)
                    proxies.Remove(proxy);
                proxy = new WatchNarrativeNpcProxy(state.Key.EntityID, visual,
                    new(WatchEntityPayloadCodec.ReadSingle(p, 8), WatchEntityPayloadCodec.ReadSingle(p, 12)));
                proxies.Add(proxy);
                level.Add(proxy);
            }
            ApplyPresentation(proxy, proxy.Sprite, proxy.Light, p, proxy.PositionSync);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
}

internal sealed class WatchAscendManagerAdapter : IWatchEntityAdapter
{
    private const int ManagerPayloadSize = 20;
    private const int HeightPayloadSize = 20;
    private const ushort HeightSubID = 1;
    private static readonly WatchAscendManagerAdapter instance = new();
    private static readonly ConditionalWeakTable<AscendManager, WatchTimedStateCache> sync = new();
    private static readonly ConditionalWeakTable<HeightDisplay, WatchTimedStateCache> heightSync = new();
    private static readonly Dictionary<int, RemoteHeightPresentation> remoteHeights = new();
    private static readonly Dictionary<AscendManager, BackgroundPresentation> backgrounds = new();

    private sealed class BackgroundPresentation
    {
        public AscendManager.Streaks? Streaks { get; set; }
        public AscendManager.Clouds? Clouds { get; set; }
    }

    private sealed class RemoteHeightPresentation
    {
        public Level Owner { get; }
        public HeightDisplay Display { get; }
        public RemoteHeightPresentation(Level owner, HeightDisplay display)
        {
            Owner = owner;
            Display = display;
        }
    }

    public WatchEntityKind Kind => WatchEntityKind.AscendManager;

    public static void Load()
    {
        On.Celeste.AscendManager.Render += Render;
        On.Celeste.HeightDisplay.Update += HeightDisplay_Update;
        On.Celeste.HeightDisplay.Removed += HeightDisplay_Removed;
        WatchEntitySyncRegistry.Register(instance);
    }
    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.HeightDisplay.Removed -= HeightDisplay_Removed;
        On.Celeste.HeightDisplay.Update -= HeightDisplay_Update;
        On.Celeste.AscendManager.Render -= Render;
        WatchEntityIDTable<AscendManager>.Clear();
        sync.Clear();
        heightSync.Clear();
        remoteHeights.Clear();
        backgrounds.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        List<AscendManager> managers = level.Entities.OfType<AscendManager>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, managers, "SummitBackgroundManager");
        foreach (AscendManager manager in managers)
        {
            if (!WatchEntityIDTable<AscendManager>.TryGet(manager, level.Session.Level, out int id)) continue;
            byte[] p = new byte[ManagerPayloadSize];
            if (manager.Dark) p[0] |= 1;
            if (manager.Ch9Ending) p[0] |= 2;
            if (manager.introLaunch) p[0] |= 4;
            if (manager.outTheTop) p[0] |= 8;
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), manager.index);
            WatchEntityPayloadCodec.WriteSingle(p, 8, manager.fade);
            WatchEntityPayloadCodec.WriteSingle(p, 12, manager.scroll);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(16), manager.background.PackedValue);
            yield return sync.GetValue(manager, static _ => new()).Capture(new(Kind, id), p, 8,
                level.TimeActive, WatchEntitySyncRegistry.IsCapturingCurrentState);
        }
        foreach (HeightDisplay display in level.Entities.OfType<HeightDisplay>())
        {
            if (display.index < 0)
                continue;
            byte[] p = new byte[HeightPayloadSize];
            if (display.Visible) p[0] |= 1;
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4), display.index);
            WatchEntityPayloadCodec.WriteSingle(p, 8, Math.Clamp(display.ease, 0f, 1f));
            WatchEntityPayloadCodec.WriteSingle(p, 12, display.approach);
            WatchEntityPayloadCodec.WriteSingle(p, 16, display.pulse);
            yield return heightSync.GetValue(display, static _ => new()).Capture(
                new(Kind, display.index, HeightSubID), p, 8,
                level.TimeActive, WatchEntitySyncRegistry.IsCapturingCurrentState);
        }
    }

    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool complete)
    {
        List<AscendManager> managers = level.Entities.OfType<AscendManager>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, managers, "SummitBackgroundManager");
        HashSet<AscendManager> desiredManagers = new();
        HashSet<int> desiredHeights = new();
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID == HeightSubID)
            {
                if (p.Length != HeightPayloadSize)
                    continue;
                int index = BinaryPrimitives.ReadInt32LittleEndian(p[4..]);
                if (index != state.Key.EntityID || index < 0 || !desiredHeights.Add(index))
                    continue;
                HeightDisplay display = GetOrCreateHeightDisplay(level, index);
                ApplyHeightDisplay(display, p);
                changed = true;
                continue;
            }

            AscendManager? manager = managers.FirstOrDefault(candidate =>
                WatchEntityIDTable<AscendManager>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
            if (manager is null || state.Key.SubID != 0 || p.Length != ManagerPayloadSize) continue;
            // Dark and Ch9Ending are immutable map configuration. The same
            // vanilla map exists on both clients, so only runtime state follows.
            manager.introLaunch = (p[0] & 4) != 0;
            manager.outTheTop = (p[0] & 8) != 0;
            manager.index = BinaryPrimitives.ReadInt32LittleEndian(p[4..]);
            manager.fade = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(p, 8), 0f, 1f);
            manager.scroll = WatchEntityPayloadCodec.ReadSingle(p, 12);
            manager.background.PackedValue = BinaryPrimitives.ReadUInt32LittleEndian(p[16..]);
            EnsureBackgroundPresentation(level, manager);
            desiredManagers.Add(manager);
            changed = true;
        }

        if (complete)
        {
            foreach (HeightDisplay display in level.Entities.OfType<HeightDisplay>().ToArray())
            {
                if (display.index >= 0 && !desiredHeights.Contains(display.index))
                {
                    RemoveHeightDisplay(display);
                    changed = true;
                }
            }
            foreach ((AscendManager manager, BackgroundPresentation presentation) in backgrounds.ToArray())
            {
                if (manager.Scene != level || !desiredManagers.Contains(manager))
                {
                    presentation.Streaks?.RemoveSelf();
                    presentation.Clouds?.RemoveSelf();
                    backgrounds.Remove(manager);
                    changed = true;
                }
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }

    private static void EnsureBackgroundPresentation(Level level, AscendManager manager)
    {
        if (!backgrounds.TryGetValue(manager, out BackgroundPresentation? presentation))
        {
            presentation = new BackgroundPresentation();
            backgrounds.Add(manager, presentation);
        }

        presentation.Streaks ??= level.Entities.OfType<AscendManager.Streaks>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.manager, manager));
        if (presentation.Streaks is null)
        {
            presentation.Streaks = new AscendManager.Streaks(manager);
            level.Add(presentation.Streaks);
        }

        if (manager.Dark)
            return;
        presentation.Clouds ??= level.Entities.OfType<AscendManager.Clouds>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.manager, manager));
        if (presentation.Clouds is null)
        {
            presentation.Clouds = new AscendManager.Clouds(manager);
            level.Add(presentation.Clouds);
        }
    }

    private static HeightDisplay GetOrCreateHeightDisplay(Level level, int index)
    {
        if (remoteHeights.TryGetValue(index, out RemoteHeightPresentation? remote)
            && ReferenceEquals(remote.Owner, level))
        {
            if (remote.Display.Scene is null && !level.Entities.ToAdd.Contains(remote.Display))
                level.Add(remote.Display);
            return remote.Display;
        }

        HeightDisplay display = level.Entities.OfType<HeightDisplay>()
            .FirstOrDefault(candidate => candidate.index == index)
            ?? new HeightDisplay(index);
        NeutralizeHeightDisplay(display);
        remoteHeights[index] = new RemoteHeightPresentation(level, display);
        if (display.Scene is null)
            level.Add(display);
        return display;
    }

    private static void ApplyHeightDisplay(HeightDisplay display, ReadOnlySpan<byte> payload)
    {
        NeutralizeHeightDisplay(display);
        display.Visible = (payload[0] & 1) != 0;
        display.ease = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(payload, 8), 0f, 1f);
        display.approach = WatchEntityPayloadCodec.ReadSingle(payload, 12);
        display.pulse = WatchEntityPayloadCodec.ReadSingle(payload, 16);
    }

    private static void NeutralizeHeightDisplay(HeightDisplay display)
    {
        foreach (Coroutine routine in display.Components.GetAll<Coroutine>())
            routine.Active = false;
        display.easingCamera = true;
        display.setAudioProgression = true;
    }

    private static void RemoveHeightDisplay(HeightDisplay display)
    {
        display.setAudioProgression = true;
        if (remoteHeights.TryGetValue(display.index, out RemoteHeightPresentation? remote)
            && ReferenceEquals(remote.Display, display))
            remoteHeights.Remove(display.index);
        display.RemoveSelf();
    }

    private static void Render(On.Celeste.AscendManager.orig_Render orig, AscendManager self)
    {
        if (MiaoNetModule.IsWatching && !MiaoNetModule.IsWatchedPlayerPaused)
            self.scroll += Engine.DeltaTime * 240f;
        orig(self);
    }

    private static void HeightDisplay_Update(
        On.Celeste.HeightDisplay.orig_Update orig,
        HeightDisplay self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        NeutralizeHeightDisplay(self);
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            orig(self);
        NeutralizeHeightDisplay(self);
    }

    private static void HeightDisplay_Removed(
        On.Celeste.HeightDisplay.orig_Removed orig,
        HeightDisplay self,
        Scene scene
    )
    {
        if (MiaoNetModule.IsWatching)
            self.setAudioProgression = true;
        orig(self, scene);
    }
}

internal sealed class WatchIntroCarAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 20;
    private static readonly WatchIntroCarAdapter instance = new();
    private static readonly ConditionalWeakTable<IntroCar, WatchTimedStateCache> sync = new();
    private static readonly ConditionalWeakTable<IntroCar, WatchRemotePosition> remote = new();
    public WatchEntityKind Kind => WatchEntityKind.IntroCar;
    public static void Load() => WatchEntitySyncRegistry.Register(instance);
    public static void Unload() { WatchEntitySyncRegistry.Unregister(instance); WatchEntityIDTable<IntroCar>.Clear(); sync.Clear(); remote.Clear(); }
    internal static void UpdatePresentation(IntroCar car)
    {
        if (!MiaoNetModule.IsWatchedPlayerPaused && remote.TryGetValue(car, out WatchRemotePosition? position))
            position.Update(car);
    }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        List<IntroCar> cars = level.Entities.OfType<IntroCar>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, cars, "introCar");
        foreach (IntroCar car in cars)
        {
            if (!WatchEntityIDTable<IntroCar>.TryGet(car, level.Session.Level, out int id)) continue;
            byte[] p = new byte[PayloadSize];
            if (car.Visible) p[0] |= 1;
            if (car.Collidable) p[0] |= 2;
            if (car.didHaveRider) p[0] |= 4;
            WatchEntityPayloadCodec.WriteSingle(p, 4, car.X);
            WatchEntityPayloadCodec.WriteSingle(p, 8, car.Y);
            WatchEntityPayloadCodec.WriteSingle(p, 12, car.startY);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(16), car.Depth);
            yield return sync.GetValue(car, static _ => new()).Capture(new(Kind, id), p, 1,
                level.TimeActive, WatchEntitySyncRegistry.IsCapturingCurrentState);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool complete)
    {
        List<IntroCar> cars = level.Entities.OfType<IntroCar>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, cars, "introCar");
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            IntroCar? car = cars.FirstOrDefault(candidate => WatchEntityIDTable<IntroCar>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
            if (car is null || state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            remote.GetValue(car, static _ => new()).Apply(car, new(WatchEntityPayloadCodec.ReadSingle(p, 4), WatchEntityPayloadCodec.ReadSingle(p, 8)));
            car.Visible = (p[0] & 1) != 0;
            car.Collidable = false;
            car.didHaveRider = (p[0] & 4) != 0;
            car.startY = WatchEntityPayloadCodec.ReadSingle(p, 12);
            car.Depth = BinaryPrimitives.ReadInt32LittleEndian(p[16..]);
        }
        return states.Count > 0 ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
}

internal sealed class WatchChapterPropAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 28;
    private static readonly WatchChapterPropAdapter instance = new();
    private static readonly ConditionalWeakTable<Entity, WatchTimedStateCache> sync = new();
    public WatchEntityKind Kind => WatchEntityKind.ChapterProp;
    public static void Load() => WatchEntitySyncRegistry.Register(instance);
    public static void Unload() { WatchEntitySyncRegistry.Unregister(instance); WatchEntityIDTable<Bonfire>.Clear(); WatchEntityIDTable<Payphone>.Clear(); sync.Clear(); }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        List<Bonfire> fires = level.Entities.OfType<Bonfire>().ToList();
        List<Payphone> phones = level.Entities.OfType<Payphone>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, fires, "bonfire");
        WatchMapEntityMatcher.AssignIDs(level, phones, "payphone");
        foreach (Bonfire fire in fires)
        {
            if (!WatchEntityIDTable<Bonfire>.TryGet(fire, level.Session.Level, out int id)) continue;
            byte[] p = CreateBase(fire, fire.sprite, fire.light, fire.bloom);
            p[1] = (byte)fire.mode;
            p[2] = (byte)Math.Max(0, fire.sprite.CurrentAnimationFrame);
            p[3] = fire.Activated ? (byte)1 : (byte)0;
            WatchEntityPayloadCodec.WriteSingle(p, 20, fire.brightness);
            WatchEntityPayloadCodec.WriteUInt16(p, 24, WatchSpriteState.EncodeAnimation(fire.sprite));
            yield return sync.GetValue(fire, static _ => new()).Capture(new(Kind, id, 1), p, 4, level.TimeActive, WatchEntitySyncRegistry.IsCapturingCurrentState);
        }
        foreach (Payphone phone in phones)
        {
            if (!WatchEntityIDTable<Payphone>.TryGet(phone, level.Session.Level, out int id)) continue;
            byte[] p = CreateBase(phone, phone.Sprite, phone.light, phone.bloom);
            p[1] = phone.Broken ? (byte)1 : (byte)0;
            p[2] = (byte)Math.Max(0, phone.Sprite.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteSingle(p, 20, phone.lightFlickerFor);
            WatchEntityPayloadCodec.WriteUInt16(p, 24, WatchSpriteState.EncodeAnimation(phone.Sprite));
            yield return sync.GetValue(phone, static _ => new()).Capture(new(Kind, id, 2), p, 4, level.TimeActive, WatchEntitySyncRegistry.IsCapturingCurrentState);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool complete)
    {
        List<Bonfire> fires = level.Entities.OfType<Bonfire>().ToList();
        List<Payphone> phones = level.Entities.OfType<Payphone>().ToList();
        WatchMapEntityMatcher.AssignIDs(level, fires, "bonfire");
        WatchMapEntityMatcher.AssignIDs(level, phones, "payphone");
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (p.Length != PayloadSize) continue;
            if (state.Key.SubID == 1)
            {
                Bonfire? fire = fires.FirstOrDefault(candidate => WatchEntityIDTable<Bonfire>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
                if (fire is null) continue;
                ApplyBase(fire, fire.sprite, fire.light, fire.bloom, p);
                fire.Activated = p[3] != 0;
                Bonfire.Mode mode = (Bonfire.Mode)p[1];
                if (fire.mode != mode)
                    fire.SetMode(mode);
                fire.brightness = WatchEntityPayloadCodec.ReadSingle(p, 20);
                WatchSpriteState.ApplyAnimation(fire.sprite, WatchEntityPayloadCodec.ReadUInt16(p, 24), p[2]);
            }
            else if (state.Key.SubID == 2)
            {
                Payphone? phone = phones.FirstOrDefault(candidate => WatchEntityIDTable<Payphone>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
                if (phone is null) continue;
                ApplyBase(phone, phone.Sprite, phone.light, phone.bloom, p);
                phone.Broken = p[1] != 0; phone.lightFlickerFor = WatchEntityPayloadCodec.ReadSingle(p, 20);
                WatchSpriteState.ApplyAnimation(phone.Sprite, WatchEntityPayloadCodec.ReadUInt16(p, 24), p[2]);
            }
        }
        return states.Count > 0 ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
    private static byte[] CreateBase(Entity entity, Sprite sprite, VertexLight light, BloomPoint bloom)
    {
        byte[] p = new byte[PayloadSize]; if (entity.Visible) p[0] |= 1; if (sprite.Visible) p[0] |= 2; if (light.Visible) p[0] |= 4;
        WatchEntityPayloadCodec.WriteSingle(p, 4, entity.X); WatchEntityPayloadCodec.WriteSingle(p, 8, entity.Y);
        WatchEntityPayloadCodec.WriteSingle(p, 12, light.Alpha); WatchEntityPayloadCodec.WriteSingle(p, 16, bloom.Alpha); return p;
    }
    private static void ApplyBase(Entity entity, Sprite sprite, VertexLight light, BloomPoint bloom, ReadOnlySpan<byte> p)
    {
        entity.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 4), WatchEntityPayloadCodec.ReadSingle(p, 8));
        entity.Visible = (p[0] & 1) != 0; sprite.Visible = (p[0] & 2) != 0; light.Visible = (p[0] & 4) != 0;
        light.Alpha = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(p, 12), 0f, 1f); bloom.Alpha = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(p, 16), 0f, 1f);
    }
}

internal sealed class WatchLookoutAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 20;
    private static readonly WatchLookoutAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.Lookout;
    public static void Load() { On.Celeste.Lookout.Interact += Interact; WatchEntitySyncRegistry.Register(instance); }
    public static void Unload() { WatchEntitySyncRegistry.Unregister(instance); On.Celeste.Lookout.Interact -= Interact; WatchEntityIDTable<Lookout>.Clear(); }
    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        List<Lookout> lookouts = level.Entities.OfType<Lookout>().ToList(); WatchMapEntityMatcher.AssignIDs(level, lookouts, "towerviewer");
        foreach (Lookout lookout in lookouts)
        {
            if (!WatchEntityIDTable<Lookout>.TryGet(lookout, level.Session.Level, out int id)) continue;
            byte[] p = new byte[PayloadSize]; if (lookout.Visible) p[0] |= 1; if (lookout.interacting) p[0] |= 2;
            p[1] = (byte)Math.Max(0, lookout.sprite.CurrentAnimationFrame); WatchEntityPayloadCodec.WriteUInt16(p, 2, WatchSpriteState.EncodeAnimation(lookout.sprite));
            WatchEntityPayloadCodec.WriteSingle(p, 4, lookout.X); WatchEntityPayloadCodec.WriteSingle(p, 8, lookout.Y);
            BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12), lookout.node); WatchEntityPayloadCodec.WriteSingle(p, 16, lookout.nodePercent);
            yield return new WatchEntityState(new(Kind, id), p);
        }
    }
    public WatchEntityApplyResult ApplyStates(Level level, IReadOnlyCollection<WatchEntityState> states, bool complete)
    {
        List<Lookout> lookouts = level.Entities.OfType<Lookout>().ToList(); WatchMapEntityMatcher.AssignIDs(level, lookouts, "towerviewer");
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span; Lookout? lookout = lookouts.FirstOrDefault(candidate => WatchEntityIDTable<Lookout>.TryGet(candidate, level.Session.Level, out int id) && id == state.Key.EntityID);
            if (lookout is null || state.Key.SubID != 0 || p.Length != PayloadSize) continue;
            lookout.Position = new(WatchEntityPayloadCodec.ReadSingle(p, 4), WatchEntityPayloadCodec.ReadSingle(p, 8)); lookout.Visible = (p[0] & 1) != 0;
            lookout.interacting = (p[0] & 2) != 0; lookout.talk.Enabled = false; lookout.node = BinaryPrimitives.ReadInt32LittleEndian(p[12..]); lookout.nodePercent = WatchEntityPayloadCodec.ReadSingle(p, 16);
            WatchSpriteState.ApplyAnimation(lookout.sprite, WatchEntityPayloadCodec.ReadUInt16(p, 2), p[1]);
        }
        return states.Count > 0 ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }
    public void ApplyEvent(Level level, WatchEntityEvent entityEvent) { }
    private static void Interact(On.Celeste.Lookout.orig_Interact orig, Lookout self, Player player) { if (!MiaoNetModule.IsWatching) orig(self, player); }
}

internal static class WatchRemotePresentationAdapter
{
    public static void Load()
    {
        On.Celeste.CoreMessage.Update += CoreMessage_Update;
        On.Celeste.Memorial.Update += Memorial_Update;
    }
    public static void Unload()
    {
        On.Celeste.Memorial.Update -= Memorial_Update;
        On.Celeste.CoreMessage.Update -= CoreMessage_Update;
    }
    private static void CoreMessage_Update(On.Celeste.CoreMessage.orig_Update orig, CoreMessage self) => WithRemotePlayer(self.Scene, () => orig(self));
    private static void Memorial_Update(On.Celeste.Memorial.orig_Update orig, Memorial self) => WithRemotePlayer(self.Scene, () => orig(self));
    private static void WithRemotePlayer(Scene scene, Action action)
    {
        if (!MiaoNetModule.IsWatching || MiaoNetModule.WatchedPlayerState is not { } state || scene.Tracker.GetEntity<Player>() is not { } player)
        { action(); return; }
        Vector2 position = player.Position; bool collidable = player.Collidable;
        player.Position = state.Position; player.Collidable = true;
        try { action(); }
        finally { player.Position = position; player.Collidable = collidable; }
    }
}
