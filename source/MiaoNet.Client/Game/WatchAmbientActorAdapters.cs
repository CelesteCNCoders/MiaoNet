using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchTimedStateCache
{
    private const float AnchorInterval = 0.1f;
    private bool hasState;
    private byte[] signature = [];
    private float nextAnchor;
    private WatchEntityState state;

    public WatchEntityState Capture(
        WatchEntityKey key,
        byte[] payload,
        int signatureLength,
        float sceneTime,
        bool force
    )
    {
        bool signatureChanged = !hasState
            || !payload.AsSpan(0, signatureLength).SequenceEqual(signature);
        if (force || signatureChanged || sceneTime >= nextAnchor)
        {
            state = new(key, payload);
            signature = payload[..signatureLength];
            hasState = true;
            nextAnchor = sceneTime + AnchorInterval;
        }
        return state;
    }
}

internal sealed class WatchRemotePosition
{
    private const float AnchorInterval = 0.1f;
    public bool HasState { get; private set; }
    private Vector2 start;
    private Vector2 target;
    private float elapsed;

    public void Apply(Entity entity, Vector2 position)
    {
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !HasState || Vector2.DistanceSquared(entity.Position, position) >= 48f * 48f;
        if (hard)
        {
            entity.Position = position;
            start = target = position;
            elapsed = AnchorInterval;
        }
        else
        {
            start = entity.Position;
            target = position;
            elapsed = 0f;
        }
        HasState = true;
    }

    public void Update(Entity entity)
    {
        if (!HasState || elapsed >= AnchorInterval)
            return;
        elapsed = Math.Min(AnchorInterval, elapsed + Engine.DeltaTime);
        entity.Position = Vector2.Lerp(start, target, elapsed / AnchorInterval);
    }
}

internal static class WatchAmbientAnimation
{
    public static byte Encode(string? id) => id switch
    {
        "idle" => 0,
        "hover" => 1,
        "hoverStressed" => 2,
        "fly" => 3,
        "sleep" => 4,
        "peck" => 5,
        "peckRare" => 6,
        _ => byte.MaxValue,
    };

    public static string? Decode(byte value) => value switch
    {
        0 => "idle",
        1 => "hover",
        2 => "hoverStressed",
        3 => "fly",
        4 => "sleep",
        5 => "peck",
        6 => "peckRare",
        _ => null,
    };

    public static void Apply(Sprite sprite, byte animation, byte frame)
    {
        string? id = Decode(animation);
        if (id is not null && sprite.Has(id))
        {
            bool changed = sprite.CurrentAnimationID != id;
            if (changed)
                sprite.Play(id, restart: true);
            int drift = Math.Abs(sprite.CurrentAnimationFrame - frame);
            if (sprite.CurrentAnimationTotalFrames > 0
                && (changed || WatchEntitySyncRegistry.IsApplyingLifecycleReset || drift > 2))
                sprite.SetAnimationFrame(Math.Min(frame, sprite.CurrentAnimationTotalFrames - 1));
        }
    }
}

internal sealed class WatchBirdNPCAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 44;
    private static readonly WatchBirdNPCAdapter instance = new();
    private static readonly ConditionalWeakTable<BirdNPC, WatchTimedStateCache> syncInfo = new();
    private static readonly ConditionalWeakTable<BirdNPC, WatchRemotePosition> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.BirdNPC;

    public static void Load()
    {
        On.Celeste.BirdNPC.ctor_EntityData_Vector2 += BirdNPC_ctor;
        On.Celeste.BirdNPC.Update += BirdNPC_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BirdNPC.Update -= BirdNPC_Update;
        On.Celeste.BirdNPC.ctor_EntityData_Vector2 -= BirdNPC_ctor;
        WatchEntityIDTable<BirdNPC>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (BirdNPC bird in level.Entities.OfType<BirdNPC>())
        {
            if (!WatchEntityIDTable<BirdNPC>.TryGet(bird, level.Session.Level, out int id))
                continue;
            byte[] payload = new byte[PayloadSize];
            if (bird.Visible) payload[0] |= 1;
            if (bird.Sprite.Visible) payload[0] |= 2;
            if (bird.Light.Visible) payload[0] |= 4;
            payload[1] = (byte)bird.mode;
            payload[2] = bird.Facing == Facings.Left ? (byte)0 : (byte)1;
            payload[3] = WatchAmbientAnimation.Encode(bird.Sprite.CurrentAnimationID);
            payload[4] = (byte)Math.Max(0, bird.Sprite.CurrentAnimationFrame);
            WriteTransform(payload, bird.Position, bird.Sprite);
            WatchEntityPayloadCodec.WriteSingle(payload, 28, bird.Light.Alpha);
            WatchEntityPayloadCodec.WriteSingle(payload, 32, bird.Sprite.Rate);
            WatchEntityPayloadCodec.WriteVector2(payload, 36, bird.Sprite.Position);
            yield return syncInfo.GetValue(bird, static _ => new()).Capture(
                new(Kind, id), payload, 4, level.TimeActive,
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
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize)
                continue;
            BirdNPC? bird = Find(level, state.Key.EntityID);
            if (bird is null)
                continue;
            remoteInfo.GetValue(bird, static _ => new()).Apply(bird, ReadPosition(p));
            bird.mode = (BirdNPC.Modes)p[1];
            bird.Facing = p[2] == 0 ? Facings.Left : Facings.Right;
            bird.Visible = (p[0] & 1) != 0;
            bird.Sprite.Visible = (p[0] & 2) != 0;
            bird.Light.Visible = (p[0] & 4) != 0;
            ApplyTransform(p, bird.Sprite);
            WatchAmbientAnimation.Apply(bird.Sprite, p[3], p[4]);
            bird.Light.Alpha = WatchEntityPayloadCodec.ReadSingle(p, 28);
            bird.Sprite.Rate = WatchEntityPayloadCodec.ReadSingle(p, 32);
            bird.Sprite.Position = WatchEntityPayloadCodec.ReadVector2(p, 36);
            bird.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static BirdNPC? Find(Level level, int id)
        => WatchEntityIDTable<BirdNPC>.Find(level, id);

    private static void BirdNPC_ctor(
        On.Celeste.BirdNPC.orig_ctor_EntityData_Vector2 orig,
        BirdNPC self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<BirdNPC>.Set(self, data.Level.Name, data.ID);
    }

    private static void BirdNPC_Update(On.Celeste.BirdNPC.orig_Update orig, BirdNPC self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        foreach (Coroutine coroutine in self.Components.GetAll<Coroutine>())
            coroutine.Active = false;
        if (!MiaoNetModule.IsWatchedPlayerPaused)
        {
            self.Components.Update();
            remoteInfo.GetValue(self, static _ => new()).Update(self);
        }
        self.Collidable = false;
    }

    internal static void WriteTransform(byte[] payload, Vector2 position, Sprite sprite)
    {
        WatchEntityPayloadCodec.WriteVector2(payload, 8, position);
        WatchEntityPayloadCodec.WriteVector2(payload, 16, sprite.Scale);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, sprite.Rotation);
    }

    internal static Vector2 ReadPosition(ReadOnlySpan<byte> payload)
        => WatchEntityPayloadCodec.ReadVector2(payload, 8);

    internal static void ApplyTransform(ReadOnlySpan<byte> payload, Sprite sprite)
    {
        sprite.Scale = WatchEntityPayloadCodec.ReadVector2(payload, 16);
        sprite.Rotation = WatchEntityPayloadCodec.ReadSingle(payload, 24);
    }
}

internal sealed class WatchFlutterBirdAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 28;
    private static readonly WatchFlutterBirdAdapter instance = new();
    private static readonly ConditionalWeakTable<FlutterBird, WatchTimedStateCache> syncInfo = new();
    private static readonly ConditionalWeakTable<FlutterBird, WatchRemotePosition> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.FlutterBird;

    public static void Load()
    {
        On.Celeste.FlutterBird.ctor += FlutterBird_ctor;
        On.Celeste.FlutterBird.Update += FlutterBird_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FlutterBird.Update -= FlutterBird_Update;
        On.Celeste.FlutterBird.ctor -= FlutterBird_ctor;
        WatchEntityIDTable<FlutterBird>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (FlutterBird bird in level.Entities.OfType<FlutterBird>())
        {
            if (!WatchEntityIDTable<FlutterBird>.TryGet(bird, level.Session.Level, out int id))
                continue;
            byte[] p = new byte[PayloadSize];
            if (bird.Visible) p[0] |= 1;
            if (bird.flyingAway) p[0] |= 2;
            p[1] = WatchAmbientAnimation.Encode(bird.sprite.CurrentAnimationID);
            p[2] = (byte)Math.Max(0, bird.sprite.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteVector2(p, 4, bird.Position);
            WatchEntityPayloadCodec.WriteVector2(p, 12, bird.sprite.Scale);
            WatchEntityPayloadCodec.WriteSingle(p, 20, bird.sprite.Rotation);
            WatchEntityPayloadCodec.WriteSingle(p, 24, bird.sprite.Rate);
            yield return syncInfo.GetValue(bird, static _ => new()).Capture(
                new(Kind, id), p, 3, level.TimeActive,
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
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize)
                continue;
            FlutterBird? bird = WatchEntityIDTable<FlutterBird>.Find(level, state.Key.EntityID);
            if (bird is null)
                continue;
            remoteInfo.GetValue(bird, static _ => new()).Apply(bird, WatchEntityPayloadCodec.ReadVector2(p, 4));
            bird.Visible = (p[0] & 1) != 0;
            bird.flyingAway = (p[0] & 2) != 0;
            bird.sprite.Scale = WatchEntityPayloadCodec.ReadVector2(p, 12);
            bird.sprite.Rotation = WatchEntityPayloadCodec.ReadSingle(p, 20);
            bird.sprite.Rate = WatchEntityPayloadCodec.ReadSingle(p, 24);
            WatchAmbientAnimation.Apply(bird.sprite, p[1], p[2]);
            bird.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void FlutterBird_ctor(
        On.Celeste.FlutterBird.orig_ctor orig,
        FlutterBird self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FlutterBird>.Set(self, data.Level.Name, data.ID);
    }

    private static void FlutterBird_Update(
        On.Celeste.FlutterBird.orig_Update orig,
        FlutterBird self
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
        {
            self.Components.Update();
            remoteInfo.GetValue(self, static _ => new()).Update(self);
        }
        self.Collidable = false;
    }
}

internal sealed class WatchMoonCreatureAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 48;
    private static readonly WatchMoonCreatureAdapter instance = new();
    private static readonly ConditionalWeakTable<MoonCreature, WatchTimedStateCache> syncInfo = new();
    private static readonly ConditionalWeakTable<MoonCreature, WatchRemotePosition> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.MoonCreature;

    public static void Load()
    {
        On.Celeste.MoonCreature.ctor_EntityData_Vector2 += MoonCreature_ctor;
        On.Celeste.MoonCreature.OnPlayer += MoonCreature_OnPlayer;
        On.Celeste.MoonCreature.Update += MoonCreature_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.MoonCreature.Update -= MoonCreature_Update;
        On.Celeste.MoonCreature.OnPlayer -= MoonCreature_OnPlayer;
        On.Celeste.MoonCreature.ctor_EntityData_Vector2 -= MoonCreature_ctor;
        WatchEntityIDTable<MoonCreature>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (MoonCreature creature in level.Entities.OfType<MoonCreature>())
        {
            if (!WatchEntityIDTable<MoonCreature>.TryGet(creature, level.Session.Level, out int id))
                continue;
            byte[] p = new byte[PayloadSize];
            if (creature.Visible) p[0] |= 1;
            if (creature.following is not null) p[0] |= 2;
            p[1] = (byte)Math.Clamp(creature.spawn, 0, byte.MaxValue);
            p[2] = WatchAmbientAnimation.Encode(creature.Sprite.CurrentAnimationID);
            p[3] = (byte)Math.Max(0, creature.Sprite.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteVector2(p, 4, creature.Position);
            WatchEntityPayloadCodec.WriteVector2(p, 12, creature.speed);
            WatchEntityPayloadCodec.WriteVector2(p, 20, creature.target);
            WatchEntityPayloadCodec.WriteVector2(p, 28, creature.bump);
            WatchEntityPayloadCodec.WriteSingle(p, 36, creature.followingTime);
            WatchEntityPayloadCodec.WriteVector2(p, 40, creature.followingOffset);
            yield return syncInfo.GetValue(creature, static _ => new()).Capture(
                new(Kind, id), p, 4, level.TimeActive,
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
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize)
                continue;
            MoonCreature? creature = WatchEntityIDTable<MoonCreature>.Find(level, state.Key.EntityID);
            if (creature is null)
                continue;
            remoteInfo.GetValue(creature, static _ => new()).Apply(
                creature,
                WatchEntityPayloadCodec.ReadVector2(p, 4)
            );
            creature.Visible = (p[0] & 1) != 0;
            creature.following = null;
            creature.speed = WatchEntityPayloadCodec.ReadVector2(p, 12);
            creature.target = WatchEntityPayloadCodec.ReadVector2(p, 20);
            creature.bump = WatchEntityPayloadCodec.ReadVector2(p, 28);
            creature.followingTime = WatchEntityPayloadCodec.ReadSingle(p, 36);
            creature.followingOffset = WatchEntityPayloadCodec.ReadVector2(p, 40);
            WatchAmbientAnimation.Apply(creature.Sprite, p[2], p[3]);
            creature.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void MoonCreature_ctor(
        On.Celeste.MoonCreature.orig_ctor_EntityData_Vector2 orig,
        MoonCreature self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<MoonCreature>.Set(self, data.Level.Name, data.ID);
    }

    private static void MoonCreature_OnPlayer(
        On.Celeste.MoonCreature.orig_OnPlayer orig,
        MoonCreature self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void MoonCreature_Update(
        On.Celeste.MoonCreature.orig_Update orig,
        MoonCreature self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (!MiaoNetModule.IsWatchedPlayerPaused)
        {
            self.Components.Update();
            remoteInfo.GetValue(self, static _ => new()).Update(self);
        }
        self.Collidable = false;
    }

}

internal sealed class WatchFlingBirdIntroAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 36;
    private static readonly WatchFlingBirdIntroAdapter instance = new();
    private static readonly ConditionalWeakTable<FlingBirdIntro, WatchTimedStateCache> syncInfo = new();
    private static readonly ConditionalWeakTable<FlingBirdIntro, WatchRemotePosition> remoteInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.FlingBirdIntro;

    public static void Load()
    {
        On.Celeste.FlingBirdIntro.ctor_EntityData_Vector2 += FlingBirdIntro_ctor;
        On.Celeste.FlingBirdIntro.OnPlayer += FlingBirdIntro_OnPlayer;
        On.Celeste.FlingBirdIntro.Update += FlingBirdIntro_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.FlingBirdIntro.Update -= FlingBirdIntro_Update;
        On.Celeste.FlingBirdIntro.OnPlayer -= FlingBirdIntro_OnPlayer;
        On.Celeste.FlingBirdIntro.ctor_EntityData_Vector2 -= FlingBirdIntro_ctor;
        WatchEntityIDTable<FlingBirdIntro>.Clear();
        syncInfo.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (FlingBirdIntro bird in level.Entities.OfType<FlingBirdIntro>())
        {
            if (!WatchEntityIDTable<FlingBirdIntro>.TryGet(bird, level.Session.Level, out int id))
                continue;
            byte[] p = new byte[PayloadSize];
            if (bird.Visible) p[0] |= 1;
            if (bird.startedRoutine) p[0] |= 2;
            if (bird.crashes) p[0] |= 4;
            if (bird.emitParticles) p[0] |= 8;
            if (bird.inCutscene) p[0] |= 16;
            p[1] = WatchAmbientAnimation.Encode(bird.Sprite.CurrentAnimationID);
            p[2] = (byte)Math.Max(0, bird.Sprite.CurrentAnimationFrame);
            WatchEntityPayloadCodec.WriteVector2(p, 4, bird.Position);
            WatchEntityPayloadCodec.WriteVector2(p, 12, bird.Sprite.Scale);
            WatchEntityPayloadCodec.WriteSingle(p, 20, bird.Sprite.Rotation);
            WatchEntityPayloadCodec.WriteVector2(p, 24, bird.BirdEndPosition);
            WatchEntityPayloadCodec.WriteSingle(p, 32, bird.Sprite.Rate);
            yield return syncInfo.GetValue(bird, static _ => new()).Capture(
                new(Kind, id), p, 3, level.TimeActive,
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
        bool changed = false;
        foreach (WatchEntityState state in states)
        {
            ReadOnlySpan<byte> p = state.Payload.Span;
            if (state.Key.SubID != 0 || p.Length != PayloadSize)
                continue;
            FlingBirdIntro? bird = WatchEntityIDTable<FlingBirdIntro>.Find(level, state.Key.EntityID);
            if (bird is null)
                continue;
            remoteInfo.GetValue(bird, static _ => new()).Apply(bird, WatchEntityPayloadCodec.ReadVector2(p, 4));
            bird.Visible = (p[0] & 1) != 0;
            bird.startedRoutine = (p[0] & 2) != 0;
            bird.emitParticles = (p[0] & 8) != 0;
            bird.inCutscene = false;
            bird.Sprite.Scale = WatchEntityPayloadCodec.ReadVector2(p, 12);
            bird.Sprite.Rotation = WatchEntityPayloadCodec.ReadSingle(p, 20);
            bird.BirdEndPosition = WatchEntityPayloadCodec.ReadVector2(p, 24);
            bird.Sprite.Rate = WatchEntityPayloadCodec.ReadSingle(p, 32);
            WatchAmbientAnimation.Apply(bird.Sprite, p[1], p[2]);
            if (bird.fakeRightWall is not null)
                bird.fakeRightWall.Collidable = false;
            bird.Collidable = false;
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static void FlingBirdIntro_ctor(
        On.Celeste.FlingBirdIntro.orig_ctor_EntityData_Vector2 orig,
        FlingBirdIntro self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<FlingBirdIntro>.Set(self, data.Level.Name, data.ID);
    }

    private static void FlingBirdIntro_OnPlayer(
        On.Celeste.FlingBirdIntro.orig_OnPlayer orig,
        FlingBirdIntro self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void FlingBirdIntro_Update(
        On.Celeste.FlingBirdIntro.orig_Update orig,
        FlingBirdIntro self
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
        {
            self.Components.Update();
            remoteInfo.GetValue(self, static _ => new()).Update(self);
        }
        if (self.fakeRightWall is not null)
            self.fakeRightWall.Collidable = false;
        self.Collidable = false;
    }
}
