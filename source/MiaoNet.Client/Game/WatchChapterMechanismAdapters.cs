using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchBirdPathAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 32;
    private const float AnchorInterval = 0.1f;
    private const byte ActiveFlag = 1 << 0;
    private const byte VisibleFlag = 1 << 1;
    private const byte TriggerEvent = 1;
    private const byte RollEvent = 2;

    private readonly record struct BirdState(
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 Speed,
        Vector2 Target,
        float Rotation
    );

    private sealed class SyncInfo
    {
        private bool hasState;
        private byte flags;
        private byte animation;
        private float nextAnchor;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, BirdState current, float time, bool force)
        {
            if (force || !hasState || flags != current.Flags || animation != current.Animation
                || ((current.Flags & ActiveFlag) != 0 && time >= nextAnchor))
            {
                state = Encode(id, current);
                flags = current.Flags;
                animation = current.Animation;
                hasState = true;
                nextAnchor = time + AnchorInterval;
            }
            return state;
        }
    }

    private sealed class RemoteInfo
    {
        public bool HasState { get; set; }
        public Vector2 Start { get; set; }
        public Vector2 Target { get; set; }
        public Vector2 TargetStart { get; set; }
        public Vector2 TargetEnd { get; set; }
        public float Elapsed { get; set; }
        public bool HasAnimation { get; set; }
        public byte Animation { get; set; }
    }

    private static readonly WatchBirdPathAdapter instance = new();
    private static readonly ConditionalWeakTable<BirdPath, SyncInfo> syncInfo = new();
    private static readonly ConditionalWeakTable<BirdPath, RemoteInfo> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.BirdPath;

    public static void Load()
    {
        On.Celeste.BirdPath.ctor_EntityID_EntityData_Vector2 += BirdPath_ctor;
        On.Celeste.BirdPath.Awake += BirdPath_Awake;
        On.Celeste.BirdPath.Trigger += BirdPath_Trigger;
        On.Celeste.BirdPath.Update += BirdPath_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BirdPath.Update -= BirdPath_Update;
        On.Celeste.BirdPath.Trigger -= BirdPath_Trigger;
        On.Celeste.BirdPath.Awake -= BirdPath_Awake;
        On.Celeste.BirdPath.ctor_EntityID_EntityData_Vector2 -= BirdPath_ctor;
        WatchEntityIDTable<BirdPath>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (BirdPath bird in WatchRoomEntityIndex.Enumerate<BirdPath>(level))
        {
            if (!WatchEntityIDTable<BirdPath>.TryGet(bird, level.Session.Level, out int id))
                continue;
            yield return syncInfo.GetValue(bird, static _ => new()).Capture(
                id,
                Capture(bird),
                level.TimeActive,
                WatchEntitySyncRegistry.IsCapturingCurrentState
            );
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, BirdState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out BirdState value)
                || !desired.TryAdd(state.Key.EntityID, value))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (BirdPath bird in WatchRoomEntityIndex.Enumerate<BirdPath>(level))
        {
            if (!WatchEntityIDTable<BirdPath>.TryGet(bird, room, out int id))
                continue;
            if (!desired.Remove(id, out BirdState state))
            {
                if (isCompleteState)
                {
                    bird.Active = bird.Visible = false;
                    remoteInfo.GetValue(bird, static _ => new()).HasState = false;
                    changed = true;
                }
                continue;
            }
            Apply(bird, state);
            changed = true;
        }

        foreach ((int id, BirdState state) in desired)
        {
            BirdPath? bird = Recreate(level, id);
            if (bird is null)
                continue;
            Apply(bird, state);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.Payload.Length != 0)
            return;
        BirdPath? bird = Find(level, entityEvent.Key.EntityID);
        if (bird is null)
            return;
        if (entityEvent.EventID == TriggerEvent)
        {
            bird.Active = bird.Visible = true;
        }
        else if (entityEvent.EventID == RollEvent)
        {
            if (bird.sprite?.Has("flyupRoll") == true
                && bird.sprite.CurrentAnimationID != "flyupRoll")
                bird.sprite.Play("flyupRoll", restart: true);
            SoundSource sound = new("event:/new_content/game/10_farewell/bird_flyuproll")
            {
                RemoveOnOneshotEnd = true,
            };
            bird.Add(sound);
        }
    }

    private static BirdState Capture(BirdPath bird)
    {
        byte flags = 0;
        if (bird.Active) flags |= ActiveFlag;
        if (bird.Visible) flags |= VisibleFlag;
        byte animation = bird.sprite?.CurrentAnimationID switch
        {
            "idle" => 0,
            "fly" => 1,
            "flyup" => 2,
            "flyupIdle" => 3,
            "flyupRoll" => 4,
            _ => byte.MaxValue,
        };
        return new(
            flags,
            animation,
            (byte)Math.Clamp(bird.sprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            bird.Position,
            bird.speed,
            bird.target,
            bird.sprite?.Rotation ?? 0f
        );
    }

    private static WatchEntityState Encode(int id, BirdState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.BirdPath, id), state, PayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                payload[1] = value.Animation;
                payload[2] = value.AnimationFrame;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteVector2(payload, 12, value.Speed);
                WatchEntityPayloadCodec.WriteVector2(payload, 20, value.Target);
                WatchEntityPayloadCodec.WriteSingle(payload, 28, value.Rotation);
            }
        );

    private static bool TryDecode(WatchEntityState state, out BirdState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.BirdPath || state.Key.SubID != 0
            || payload.Length != PayloadSize || (payload[0] & ~0b0000_0011) != 0
            || (payload[1] > 4 && payload[1] != byte.MaxValue) || payload[3] != 0)
            return false;
        float[] numbers = [
            WatchEntityPayloadCodec.ReadSingle(payload, 4),
            WatchEntityPayloadCodec.ReadSingle(payload, 8),
            WatchEntityPayloadCodec.ReadSingle(payload, 12),
            WatchEntityPayloadCodec.ReadSingle(payload, 16),
            WatchEntityPayloadCodec.ReadSingle(payload, 20),
            WatchEntityPayloadCodec.ReadSingle(payload, 24),
            WatchEntityPayloadCodec.ReadSingle(payload, 28),
        ];
        if (numbers.Any(number => !float.IsFinite(number)))
            return false;
        value = new(payload[0], payload[1], payload[2],
            new(numbers[0], numbers[1]), new(numbers[2], numbers[3]),
            new(numbers[4], numbers[5]), numbers[6]);
        return true;
    }

    private static void Apply(BirdPath bird, BirdState state)
    {
        RemoteInfo applied = remoteInfo.GetValue(bird, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || Vector2.DistanceSquared(bird.Position, state.Position) >= 96f * 96f;
        if (hard)
        {
            bird.Position = state.Position;
            applied.Start = applied.Target = state.Position;
            bird.target = state.Target;
            applied.TargetStart = applied.TargetEnd = state.Target;
            applied.Elapsed = AnchorInterval;
        }
        else
        {
            applied.Start = bird.Position;
            applied.Target = state.Position;
            applied.TargetStart = bird.target;
            applied.TargetEnd = state.Target;
            applied.Elapsed = 0f;
        }
        applied.HasState = true;
        bird.Active = (state.Flags & ActiveFlag) != 0;
        bird.Visible = (state.Flags & VisibleFlag) != 0;
        bird.speed = state.Speed;
        if (bird.sprite is null)
            return;
        string? animation = state.Animation switch
        {
            0 => "idle", 1 => "fly", 2 => "flyup", 3 => "flyupIdle", 4 => "flyupRoll",
            _ => null,
        };
        if (animation is not null && bird.sprite.Has(animation))
        {
            bool animationChanged = !applied.HasAnimation || applied.Animation != state.Animation;
            if (animationChanged)
                bird.sprite.Play(animation, restart: true);
            if (hard && bird.sprite.CurrentAnimationTotalFrames > 0)
                bird.sprite.SetAnimationFrame(Math.Min(
                    state.AnimationFrame,
                    bird.sprite.CurrentAnimationTotalFrames - 1
                ));
            applied.HasAnimation = true;
            applied.Animation = state.Animation;
        }
        bird.sprite.Rotation = state.Rotation;
    }

    private static BirdPath? Find(Level level, int id)
        => WatchEntityIDTable<BirdPath>.Find(level, id);

    private static BirdPath? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "birdPath"
        );
        if (data is null)
            return null;
        BirdPath bird = new(new EntityID(level.Session.Level, id), data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<BirdPath>.Set(bird, level.Session.Level, id);
        level.Add(bird);
        return bird;
    }

    private static void BirdPath_ctor(
        On.Celeste.BirdPath.orig_ctor_EntityID_EntityData_Vector2 orig,
        BirdPath self,
        EntityID id,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, id, data, offset);
        WatchEntityIDTable<BirdPath>.Set(self, id.Level, id.ID);
    }

    private static void BirdPath_Trigger(On.Celeste.BirdPath.orig_Trigger orig, BirdPath self)
    {
        if (MiaoNetModule.IsWatching)
            return;
        orig(self);
        if (self.Scene is Level level
            && WatchEntityIDTable<BirdPath>.TryGet(self, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.BirdPath, id), TriggerEvent, []
            ));
    }

    private static void BirdPath_Awake(
        On.Celeste.BirdPath.orig_Awake orig,
        BirdPath self,
        Scene scene
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self, scene);
            return;
        }

        // onlyIfLeft is a local-player spawn filter. The hidden watcher Player
        // must never decide whether the remote bird exists.
        bool onlyIfLeft = self.onlyIfLeft;
        self.onlyIfLeft = false;
        try
        {
            orig(self, scene);
        }
        finally
        {
            self.onlyIfLeft = onlyIfLeft;
        }
    }

    private static void BirdPath_Update(On.Celeste.BirdPath.orig_Update orig, BirdPath self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            string? animation = self.sprite?.CurrentAnimationID;
            orig(self);
            if (animation != "flyupRoll" && self.sprite?.CurrentAnimationID == "flyupRoll"
                && self.Scene is Level level
                && WatchEntityIDTable<BirdPath>.TryGet(
                    self,
                    level.Session.Level,
                    out int id
                ))
            {
                WatchEntitySyncRegistry.PublishEvent(level, new(
                    new WatchEntityKey(WatchEntityKind.BirdPath, id), RollEvent, []
                ));
            }
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        foreach (Coroutine coroutine in self.Components.GetAll<Coroutine>())
            coroutine.Active = false;
        if (remoteInfo.TryGetValue(self, out RemoteInfo? applied)
            && applied.HasState)
        {
            if (applied.Elapsed < AnchorInterval)
            {
                applied.Elapsed = Math.Min(AnchorInterval, applied.Elapsed + Engine.DeltaTime);
                float progress = applied.Elapsed / AnchorInterval;
                self.target = Vector2.Lerp(applied.TargetStart, applied.TargetEnd, progress);
            }
            else
            {
                self.target = applied.TargetEnd;
            }
        }
        orig(self);
        if (applied is not null && applied.HasState)
        {
            float progress = applied.Elapsed / AnchorInterval;
            Vector2 expected = Vector2.Lerp(applied.Start, applied.Target, progress);
            float correction = 1f - MathF.Pow(0.001f, Engine.DeltaTime);
            self.Position = Vector2.Lerp(self.Position, expected, correction);
        }
    }
}

internal sealed class WatchWhiteBlockAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 12;
    private const byte EnabledFlag = 1 << 0;
    private const byte ActivatedFlag = 1 << 1;
    private const byte VisibleFlag = 1 << 2;
    private const byte CollidableFlag = 1 << 3;
    private const byte BackgroundFlag = 1 << 4;
    private const byte ActivateEvent = 1;

    private static readonly WatchWhiteBlockAdapter instance = new();
    public WatchEntityKind Kind => WatchEntityKind.WhiteBlock;

    public static void Load()
    {
        On.Celeste.WhiteBlock.ctor += WhiteBlock_ctor;
        On.Celeste.WhiteBlock.Update += WhiteBlock_Update;
        On.Celeste.WhiteBlock.Activate += WhiteBlock_Activate;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.WhiteBlock.Activate -= WhiteBlock_Activate;
        On.Celeste.WhiteBlock.Update -= WhiteBlock_Update;
        On.Celeste.WhiteBlock.ctor -= WhiteBlock_ctor;
        WatchEntityIDTable<WhiteBlock>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (WhiteBlock block in WatchRoomEntityIndex.Enumerate<WhiteBlock>(level))
        {
            if (!WatchEntityIDTable<WhiteBlock>.TryGet(block, level.Session.Level, out int id))
                continue;
            byte flags = 0;
            if (block.enabled) flags |= EnabledFlag;
            if (block.activated) flags |= ActivatedFlag;
            if (block.Visible) flags |= VisibleFlag;
            if (block.Collidable) flags |= CollidableFlag;
            if (block.bgSolidTiles?.Scene is not null) flags |= BackgroundFlag;
            var current = (Flags: flags, block.playerDuckTimer, block.Depth);
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    WatchEntityPayloadCodec.WriteSingle(payload, 4, state.playerDuckTimer);
                    BitConverter.TryWriteBytes(payload[8..], state.Depth);
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
        Dictionary<int, ReadOnlyMemory<byte>> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state.Payload))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        string room = level.Session.Level;
        Dictionary<int, WhiteBlock> existing = WatchRoomEntityIndex.Enumerate<WhiteBlock>(level)
            .Where(block => WatchEntityIDTable<WhiteBlock>.TryGet(block, room, out _))
            .ToDictionary(
                block => { WatchEntityIDTable<WhiteBlock>.TryGet(block, room, out int id); return id; },
                block => block
            );
        foreach ((int id, ReadOnlyMemory<byte> memory) in desired)
        {
            ReadOnlySpan<byte> payload = memory.Span;
            bool activated = (payload[0] & ActivatedFlag) != 0;
            bool backgroundPresent = (payload[0] & BackgroundFlag) != 0;
            if (!existing.Remove(id, out WhiteBlock? block))
            {
                block = Recreate(level, id);
                if (block is null)
                    continue;
            }
            if (!activated && block.activated)
            {
                block = Recreate(level, id, block);
                if (block is null)
                    continue;
            }
            if (activated && (!block.activated
                || (backgroundPresent && block.bgSolidTiles?.Scene is null)))
            {
                Player? player = level.Tracker.GetEntity<Player>();
                if (player is not null)
                {
                    block.activated = false;
                    block.Activate(player);
                }
            }
            block.enabled = (payload[0] & EnabledFlag) != 0;
            block.activated = activated;
            block.Visible = (payload[0] & VisibleFlag) != 0;
            block.Collidable = false;
            block.playerDuckTimer = WatchEntityPayloadCodec.ReadSingle(payload, 4);
            block.Depth = BitConverter.ToInt32(payload[8..]);
            changed = true;
        }
        if (isCompleteState)
        {
            foreach (WhiteBlock block in existing.Values)
            {
                block.Visible = block.Collidable = false;
                changed = true;
            }
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != ActivateEvent || entityEvent.Payload.Length != 0)
            return;
        WhiteBlock? block = WatchEntityIDTable<WhiteBlock>.Find(level, entityEvent.Key.EntityID);
        if (block is null || block.activated)
            return;
        Player? player = level.Tracker.GetEntity<Player>();
        if (player is not null)
            block.Activate(player);
        block.Collidable = false;
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.WhiteBlock || state.Key.SubID != 0
            || payload.Length != PayloadSize || (payload[0] & ~0b0001_1111) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        float timer = WatchEntityPayloadCodec.ReadSingle(payload, 4);
        return float.IsFinite(timer) && timer >= 0f && timer <= 10f;
    }

    private static WhiteBlock? Recreate(Level level, int id, WhiteBlock? old = null)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name.Equals("whiteblock", StringComparison.OrdinalIgnoreCase)
        );
        if (data is null)
            return null;
        if (old is not null)
        {
            old.bgSolidTiles?.RemoveSelf();
            old.RemoveSelf();
        }
        WhiteBlock block = new(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<WhiteBlock>.Set(block, level.Session.Level, id);
        level.Add(block);
        return block;
    }

    private static void WhiteBlock_ctor(
        On.Celeste.WhiteBlock.orig_ctor orig,
        WhiteBlock self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<WhiteBlock>.Set(self, data.Level.Name, data.ID);
    }

    private static void WhiteBlock_Update(On.Celeste.WhiteBlock.orig_Update orig, WhiteBlock self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            self.Components.Update();
        self.Collidable = false;
    }

    private static void WhiteBlock_Activate(
        On.Celeste.WhiteBlock.orig_Activate orig,
        WhiteBlock self,
        Player player
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        if (MiaoNetModule.IsWatching)
        {
            int playerDepth = player.Depth;
            try
            {
                orig(self, player);
            }
            finally
            {
                // Vanilla changes the triggering Player's depth permanently.
                // The watcher Player is hidden infrastructure, not the actor.
                player.Depth = playerDepth;
            }
        }
        else
            orig(self, player);
        if (!MiaoNetModule.IsWatching && self.Scene is Level level
            && WatchEntityIDTable<WhiteBlock>.TryGet(self, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.WhiteBlock, id), ActivateEvent, []
            ));
    }
}

internal sealed class WatchRidgeGateAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 24;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte NodeFlag = 1 << 2;
    private const byte EnterEvent = 1;
    private static readonly WatchRidgeGateAdapter instance = new();

    public WatchEntityKind Kind => WatchEntityKind.RidgeGate;

    public static void Load()
    {
        On.Celeste.RidgeGate.ctor_EntityData_Vector2 += RidgeGate_ctor;
        On.Celeste.RidgeGate.Awake += RidgeGate_Awake;
        On.Celeste.RidgeGate.EnterSequence += RidgeGate_EnterSequence;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.RidgeGate.EnterSequence -= RidgeGate_EnterSequence;
        On.Celeste.RidgeGate.Awake -= RidgeGate_Awake;
        On.Celeste.RidgeGate.ctor_EntityData_Vector2 -= RidgeGate_ctor;
        WatchEntityIDTable<RidgeGate>.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (RidgeGate gate in WatchRoomEntityIndex.Enumerate<RidgeGate>(level))
        {
            if (!WatchEntityIDTable<RidgeGate>.TryGet(gate, level.Session.Level, out int id))
                continue;
            byte flags = 0;
            if (gate.Visible) flags |= VisibleFlag;
            if (gate.Collidable) flags |= CollidableFlag;
            if (gate.node.HasValue) flags |= NodeFlag;
            var current = (Flags: flags, gate.Position, Node: gate.node ?? Vector2.Zero, gate.Depth);
            yield return WatchEntityState.FromTyped(
                new(Kind, id), current, PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    WatchEntityPayloadCodec.WriteVector2(payload, 4, state.Position);
                    WatchEntityPayloadCodec.WriteVector2(payload, 12, state.Node);
                    BitConverter.TryWriteBytes(payload[20..], state.Depth);
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
        Dictionary<int, ReadOnlyMemory<byte>> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state.Payload))
                return WatchEntityApplyResult.None;
        }
        bool changed = false;
        string room = level.Session.Level;
        foreach (RidgeGate gate in WatchRoomEntityIndex.Enumerate<RidgeGate>(level))
        {
            if (!WatchEntityIDTable<RidgeGate>.TryGet(gate, room, out int id))
                continue;
            if (!desired.Remove(id, out ReadOnlyMemory<byte> memory))
            {
                if (isCompleteState)
                {
                    gate.Visible = gate.Collidable = false;
                    changed = true;
                }
                continue;
            }
            Apply(gate, memory.Span);
            changed = true;
        }
        foreach ((int id, ReadOnlyMemory<byte> memory) in desired)
        {
            RidgeGate? gate = Recreate(level, id);
            if (gate is null)
                continue;
            Apply(gate, memory.Span);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID == EnterEvent && entityEvent.Payload.Length == 0)
        {
            RidgeGate? gate = WatchEntityIDTable<RidgeGate>.Find(level, entityEvent.Key.EntityID);
            if (gate is not null)
                Audio.Play("event:/game/04_cliffside/stone_blockade", gate.Position);
        }
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.RidgeGate || state.Key.SubID != 0
            || payload.Length != PayloadSize || (payload[0] & ~0b0000_0111) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        return float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 4))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 12))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 16));
    }

    private static void Apply(RidgeGate gate, ReadOnlySpan<byte> payload)
    {
        gate.Position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
        gate.node = (payload[0] & NodeFlag) != 0
            ? WatchEntityPayloadCodec.ReadVector2(payload, 12)
            : null;
        gate.Visible = (payload[0] & VisibleFlag) != 0;
        gate.Collidable = false;
        gate.Depth = BitConverter.ToInt32(payload[20..]);
    }

    private static RidgeGate? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "ridgeGate"
        );
        if (data is null)
            return null;
        RidgeGate gate = new(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<RidgeGate>.Set(gate, level.Session.Level, id);
        level.Add(gate);
        return gate;
    }

    private static void RidgeGate_ctor(
        On.Celeste.RidgeGate.orig_ctor_EntityData_Vector2 orig,
        RidgeGate self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<RidgeGate>.Set(self, data.Level.Name, data.ID);
    }

    private static void RidgeGate_Awake(
        On.Celeste.RidgeGate.orig_Awake orig,
        RidgeGate self,
        Scene scene
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self, scene);
            return;
        }
        self.Collidable = false;
        orig(self, scene);
        self.Collidable = false;
    }

    private static System.Collections.IEnumerator RidgeGate_EnterSequence(
        On.Celeste.RidgeGate.orig_EnterSequence orig,
        RidgeGate self,
        Vector2 moveTo
    )
    {
        if (MiaoNetModule.IsWatching)
            return EmptyRoutine();
        if (self.Scene is Level level
            && WatchEntityIDTable<RidgeGate>.TryGet(self, level.Session.Level, out int id))
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.RidgeGate, id), EnterEvent, []
            ));
        return orig(self, moveTo);
    }

    private static System.Collections.IEnumerator EmptyRoutine()
    {
        yield break;
    }
}
