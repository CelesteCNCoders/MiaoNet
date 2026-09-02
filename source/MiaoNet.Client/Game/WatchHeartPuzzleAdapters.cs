using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchForsakenCitySatelliteAdapter : IWatchEntityAdapter
{
    private const int ControllerPayloadSize = 32;
    private const int BirdPayloadSize = 48;
    private const float BirdAnchorInterval = 0.1f;
    private const byte EnabledFlag = 1 << 0;
    private const byte UnlockedFlag = 1 << 1;
    private const byte GemPresentFlag = 1 << 2;
    private const byte PulseVisibleFlag = 1 << 3;
    private const byte ScreenVisibleFlag = 1 << 4;
    private const byte ScreenNoiseVisibleFlag = 1 << 5;
    private const byte ScreenShineVisibleFlag = 1 << 6;
    private const byte ScreenBloomVisibleFlag = 1 << 7;
    private const byte BirdActiveFlag = 1 << 0;
    private const byte BirdVisibleFlag = 1 << 1;
    private const byte BirdHeartPresentFlag = 1 << 2;
    private const byte BirdSpriteVisibleFlag = 1 << 3;
    private const byte BirdHeartVisibleFlag = 1 << 4;
    private const byte UnlockEvent = 1;
    private const byte BirdDashEvent = 2;
    private const byte BirdTransformEvent = 3;

    private readonly record struct ControllerState(
        byte Flags,
        byte InputCount,
        ulong Inputs,
        Vector2 HeartPosition,
        uint PulseColor,
        uint ScreenColor,
        float PulseBloomAlpha,
        float ScreenBloomAlpha
    );

    private readonly record struct BirdState(
        byte Flags,
        byte Animation,
        byte AnimationFrame,
        Vector2 Position,
        Vector2 Speed,
        float Timer,
        Vector2 Scale,
        float Rotation,
        Vector2 HeartScale,
        uint SpriteColor
    );

    private sealed class BirdSyncInfo
    {
        private bool hasState;
        private byte flags;
        private byte animation;
        private float nextAnchor;
        private WatchEntityState state;

        public WatchEntityState Capture(
            int id,
            ushort subID,
            BirdState current,
            float sceneTime,
            bool force
        )
        {
            bool continuous = (current.Flags
                & (BirdActiveFlag | BirdVisibleFlag | BirdHeartPresentFlag)) != 0;
            if (force || !hasState || flags != current.Flags || animation != current.Animation
                || (continuous && sceneTime >= nextAnchor))
            {
                state = EncodeBird(id, subID, current);
                flags = current.Flags;
                animation = current.Animation;
                hasState = true;
                nextAnchor = sceneTime + BirdAnchorInterval;
            }
            return state;
        }
    }

    private sealed class ControllerSyncInfo
    {
        private bool hasState;
        private float nextAnchor;
        private ControllerState previous;
        private WatchEntityState state;

        public WatchEntityState Capture(int id, ControllerState current, float sceneTime, bool force)
        {
            bool signatureChanged = !hasState
                || current.Flags != previous.Flags
                || current.InputCount != previous.InputCount
                || current.Inputs != previous.Inputs
                || current.PulseColor != previous.PulseColor
                || current.ScreenColor != previous.ScreenColor;
            bool continuousChanged = hasState
                && (current.HeartPosition != previous.HeartPosition
                    || current.PulseBloomAlpha != previous.PulseBloomAlpha
                    || current.ScreenBloomAlpha != previous.ScreenBloomAlpha);
            if (force || signatureChanged || (continuousChanged && sceneTime >= nextAnchor))
            {
                state = EncodeController(id, current);
                previous = current;
                hasState = true;
                nextAnchor = sceneTime + BirdAnchorInterval;
            }
            return state;
        }
    }

    private sealed class BirdRemoteInfo
    {
        public bool HasState { get; set; }
        public Vector2 Start { get; set; }
        public Vector2 Target { get; set; }
        public float Elapsed { get; set; }
        public bool AllowRoutine { get; set; }
    }

    private sealed class BirdOwner
    {
        public int ID { get; init; }
        public ushort SubID { get; init; }
        public string Level { get; init; } = string.Empty;
    }

    private static readonly WatchForsakenCitySatelliteAdapter instance = new();
    private static readonly ConditionalWeakTable<ForsakenCitySatellite, ControllerSyncInfo>
        controllerSyncInfo = new();
    private static readonly ConditionalWeakTable<ForsakenCitySatellite.CodeBird, BirdSyncInfo>
        birdSyncInfo = new();
    private static readonly ConditionalWeakTable<ForsakenCitySatellite.CodeBird, BirdRemoteInfo>
        birdRemoteInfo = new();
    private static readonly ConditionalWeakTable<ForsakenCitySatellite.CodeBird, BirdOwner>
        birdOwners = new();
    public WatchEntityKind Kind => WatchEntityKind.ForsakenCitySatellite;

    public static void Load()
    {
        On.Celeste.ForsakenCitySatellite.ctor += Satellite_ctor;
        On.Celeste.ForsakenCitySatellite.Added += Satellite_Added;
        On.Celeste.ForsakenCitySatellite.Update += Satellite_Update;
        On.Celeste.ForsakenCitySatellite.UnlockGem += Satellite_UnlockGem;
        On.Celeste.ForsakenCitySatellite.CodeBird.Update += CodeBird_Update;
        On.Celeste.ForsakenCitySatellite.CodeBird.Dash += CodeBird_Dash;
        On.Celeste.ForsakenCitySatellite.CodeBird.Transform += CodeBird_Transform;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ForsakenCitySatellite.CodeBird.Transform -= CodeBird_Transform;
        On.Celeste.ForsakenCitySatellite.CodeBird.Dash -= CodeBird_Dash;
        On.Celeste.ForsakenCitySatellite.CodeBird.Update -= CodeBird_Update;
        On.Celeste.ForsakenCitySatellite.UnlockGem -= Satellite_UnlockGem;
        On.Celeste.ForsakenCitySatellite.Update -= Satellite_Update;
        On.Celeste.ForsakenCitySatellite.Added -= Satellite_Added;
        On.Celeste.ForsakenCitySatellite.ctor -= Satellite_ctor;
        WatchEntityIDTable<ForsakenCitySatellite>.Clear();
        controllerSyncInfo.Clear();
        birdSyncInfo.Clear();
        birdRemoteInfo.Clear();
        birdOwners.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (ForsakenCitySatellite satellite in WatchRoomEntityIndex.Enumerate<ForsakenCitySatellite>(level))
        {
            if (!WatchEntityIDTable<ForsakenCitySatellite>.TryGet(
                satellite,
                level.Session.Level,
                out int id
            ))
                continue;

            byte flags = 0;
            if (satellite.enabled) flags |= EnabledFlag;
            if (level.Session.GetFlag("unlocked_satellite")) flags |= UnlockedFlag;
            HeartGem? heart = FindSatelliteHeart(level, satellite);
            if (heart is not null)
                flags |= GemPresentFlag;
            if (satellite.pulse?.Visible == true) flags |= PulseVisibleFlag;
            if (satellite.computerScreen?.Visible == true) flags |= ScreenVisibleFlag;
            if (satellite.computerScreenNoise?.Visible == true) flags |= ScreenNoiseVisibleFlag;
            if (satellite.computerScreenShine?.Visible == true) flags |= ScreenShineVisibleFlag;
            if (satellite.screenBloom?.Visible == true) flags |= ScreenBloomVisibleFlag;
            (byte inputCount, ulong acceptedInputs) = PackInputs(satellite.currentInputs);
            Vector2 heartPosition = heart?.Position ?? satellite.gemSpawnPosition;
            ControllerState controller = new(
                flags,
                inputCount,
                acceptedInputs,
                heartPosition,
                satellite.pulse?.Color.PackedValue ?? Color.White.PackedValue,
                satellite.computerScreen?.Color.PackedValue ?? Color.White.PackedValue,
                satellite.pulseBloom?.Alpha ?? 0f,
                satellite.screenBloom?.Alpha ?? 0f
            );
            yield return controllerSyncInfo.GetValue(satellite, static _ => new()).Capture(
                id,
                controller,
                level.TimeActive,
                WatchEntitySyncRegistry.IsCapturingCurrentState
            );

            foreach (ForsakenCitySatellite.CodeBird bird in satellite.birds)
            {
                ushort subID = EncodeDirection(bird.code);
                if (subID is 0 or > 5)
                    continue;
                yield return birdSyncInfo.GetValue(bird, static _ => new()).Capture(
                    id,
                    subID,
                    CaptureBird(bird),
                    level.TimeActive,
                    WatchEntitySyncRegistry.IsCapturingCurrentState
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
        Dictionary<int, ControllerState> controllers = new();
        Dictionary<(int ID, ushort SubID), BirdState> birds = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.SubID == 0)
            {
                if (!TryDecodeController(state, out ControllerState value)
                    || !controllers.TryAdd(state.Key.EntityID, value))
                    return WatchEntityApplyResult.None;
            }
            else if (!TryDecodeBird(state, out BirdState bird)
                || !birds.TryAdd((state.Key.EntityID, state.Key.SubID), bird))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        foreach (ForsakenCitySatellite satellite in WatchRoomEntityIndex.Enumerate<ForsakenCitySatellite>(level))
        {
            if (!WatchEntityIDTable<ForsakenCitySatellite>.TryGet(satellite, room, out int id))
                continue;
            if (!controllers.Remove(id, out ControllerState controller))
            {
                if (isCompleteState)
                {
                    satellite.Visible = false;
                    satellite.Active = false;
                    changed = true;
                }
                continue;
            }
            ApplyController(level, satellite, controller);
            foreach (ForsakenCitySatellite.CodeBird bird in satellite.birds)
            {
                ushort subID = EncodeDirection(bird.code);
                if (birds.Remove((id, subID), out BirdState birdState))
                    ApplyBird(bird, birdState);
                else if (isCompleteState)
                    bird.Visible = bird.Active = false;
            }
            changed = true;
        }

        foreach ((int id, ControllerState controller) in controllers)
        {
            ForsakenCitySatellite? satellite = Recreate(level, id);
            if (satellite is null)
                continue;
            ApplyController(level, satellite, controller);
            foreach (ForsakenCitySatellite.CodeBird bird in satellite.birds)
            {
                ushort subID = EncodeDirection(bird.code);
                if (birds.Remove((id, subID), out BirdState birdState))
                    ApplyBird(bird, birdState);
            }
            changed = true;
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        ForsakenCitySatellite? satellite = Find(level, entityEvent.Key.EntityID);
        if (satellite is null)
            return;
        if (entityEvent.Key.SubID == 0 && entityEvent.EventID == UnlockEvent
            && entityEvent.Payload.Length == 0)
        {
            satellite.enabled = false;
            Audio.Play("event:/game/01_forsaken_city/birdbros_finish", satellite.birdFlyPosition);
            return;
        }

        ForsakenCitySatellite.CodeBird? bird = satellite.birds.FirstOrDefault(candidate =>
            EncodeDirection(candidate.code) == entityEvent.Key.SubID
        );
        if (bird is null)
            return;
        BirdRemoteInfo applied = birdRemoteInfo.GetValue(bird, static _ => new());
        if (entityEvent.EventID == BirdDashEvent && entityEvent.Payload.Length == 0)
        {
            applied.AllowRoutine = true;
            bird.Dash();
        }
        else if (entityEvent.EventID == BirdTransformEvent
            && entityEvent.Payload.Length == 4)
        {
            float delay = WatchEntityPayloadCodec.ReadSingle(entityEvent.Payload.Span, 0);
            if (!float.IsFinite(delay) || delay < 0f || delay > 3f)
                return;
            applied.AllowRoutine = true;
            bird.Transform(delay);
        }
    }

    private static bool TryDecodeController(
        WatchEntityState state,
        out ControllerState value
    )
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.ForsakenCitySatellite
            || state.Key.SubID != 0
            || payload.Length != ControllerPayloadSize
            || payload[1] > 6)
            return false;
        ulong inputs = 0;
        for (int index = 0; index < 6; index++)
        {
            byte input = payload[2 + index];
            if (index < payload[1] ? input is 0 or > 5 : input != 0)
                return false;
            inputs |= (ulong)input << (index * 8);
        }
        Vector2 heartPosition = WatchEntityPayloadCodec.ReadVector2(payload, 8);
        if (!float.IsFinite(heartPosition.X) || !float.IsFinite(heartPosition.Y))
            return false;
        float pulseBloomAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 24);
        float screenBloomAlpha = WatchEntityPayloadCodec.ReadSingle(payload, 28);
        if (!float.IsFinite(pulseBloomAlpha) || !float.IsFinite(screenBloomAlpha)
            || pulseBloomAlpha is < 0f or > 2f || screenBloomAlpha is < 0f or > 2f)
            return false;
        value = new(
            payload[0],
            payload[1],
            inputs,
            heartPosition,
            BitConverter.ToUInt32(payload[16..20]),
            BitConverter.ToUInt32(payload[20..24]),
            pulseBloomAlpha,
            screenBloomAlpha
        );
        return true;
    }

    private static bool TryDecodeBird(WatchEntityState state, out BirdState value)
    {
        value = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.ForsakenCitySatellite
            || state.Key.SubID is 0 or > 5
            || payload.Length != BirdPayloadSize
            || (payload[0] & ~0b0001_1111) != 0
            || (payload[1] > 5 && payload[1] != byte.MaxValue)
            || payload[3] != 0)
            return false;
        float[] values = [
            WatchEntityPayloadCodec.ReadSingle(payload, 4),
            WatchEntityPayloadCodec.ReadSingle(payload, 8),
            WatchEntityPayloadCodec.ReadSingle(payload, 12),
            WatchEntityPayloadCodec.ReadSingle(payload, 16),
            WatchEntityPayloadCodec.ReadSingle(payload, 20),
            WatchEntityPayloadCodec.ReadSingle(payload, 24),
            WatchEntityPayloadCodec.ReadSingle(payload, 28),
            WatchEntityPayloadCodec.ReadSingle(payload, 32),
            WatchEntityPayloadCodec.ReadSingle(payload, 36),
            WatchEntityPayloadCodec.ReadSingle(payload, 40),
        ];
        if (values.Any(number => !float.IsFinite(number)))
            return false;
        value = new(payload[0], payload[1], payload[2],
            new(values[0], values[1]), new(values[2], values[3]),
            values[4], new(values[5], values[6]), values[7],
            new(values[8], values[9]), BitConverter.ToUInt32(payload[44..]));
        return true;
    }

    private static BirdState CaptureBird(ForsakenCitySatellite.CodeBird bird)
    {
        bool present = bird.Scene is not null;
        byte flags = 0;
        if (present && bird.Active) flags |= BirdActiveFlag;
        if (present && bird.Visible) flags |= BirdVisibleFlag;
        if (present && bird.heartImage is not null) flags |= BirdHeartPresentFlag;
        if (present && bird.sprite?.Visible == true) flags |= BirdSpriteVisibleFlag;
        if (present && bird.heartImage?.Visible == true) flags |= BirdHeartVisibleFlag;
        return new(
            flags,
            EncodeBirdAnimation(bird.sprite?.CurrentAnimationID),
            (byte)Math.Clamp(bird.sprite?.CurrentAnimationFrame ?? 0, 0, byte.MaxValue),
            bird.Position,
            bird.speed,
            bird.timer,
            bird.sprite?.Scale ?? Vector2.One,
            bird.sprite?.Rotation ?? 0f,
            bird.heartImage?.Scale ?? Vector2.Zero,
            bird.sprite?.Color.PackedValue ?? Color.White.PackedValue
        );
    }

    private static WatchEntityState EncodeBird(int id, ushort subID, BirdState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.ForsakenCitySatellite, id, subID),
            state,
            BirdPayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                payload[1] = value.Animation;
                payload[2] = value.AnimationFrame;
                WatchEntityPayloadCodec.WriteVector2(payload, 4, value.Position);
                WatchEntityPayloadCodec.WriteVector2(payload, 12, value.Speed);
                WatchEntityPayloadCodec.WriteSingle(payload, 20, value.Timer);
                WatchEntityPayloadCodec.WriteVector2(payload, 24, value.Scale);
                WatchEntityPayloadCodec.WriteSingle(payload, 32, value.Rotation);
                WatchEntityPayloadCodec.WriteVector2(payload, 36, value.HeartScale);
                BitConverter.TryWriteBytes(payload[44..], value.SpriteColor);
            }
        );

    private static WatchEntityState EncodeController(int id, ControllerState state)
        => WatchEntityState.FromTyped(
            new(WatchEntityKind.ForsakenCitySatellite, id),
            state,
            ControllerPayloadSize,
            static (payload, value) =>
            {
                payload[0] = value.Flags;
                payload[1] = value.InputCount;
                for (int index = 0; index < value.InputCount; index++)
                    payload[2 + index] = (byte)(value.Inputs >> (index * 8));
                WatchEntityPayloadCodec.WriteVector2(payload, 8, value.HeartPosition);
                BitConverter.TryWriteBytes(payload[16..], value.PulseColor);
                BitConverter.TryWriteBytes(payload[20..], value.ScreenColor);
                WatchEntityPayloadCodec.WriteSingle(payload, 24, value.PulseBloomAlpha);
                WatchEntityPayloadCodec.WriteSingle(payload, 28, value.ScreenBloomAlpha);
            }
        );

    private static void ApplyController(
        Level level,
        ForsakenCitySatellite satellite,
        ControllerState state
    )
    {
        satellite.Visible = satellite.Active = true;
        satellite.enabled = (state.Flags & EnabledFlag) != 0;
        DisableLocalPresentation(satellite);
        satellite.currentInputs.Clear();
        for (int i = 0; i < state.InputCount; i++)
            satellite.currentInputs.Add(DecodeDirection((byte)(state.Inputs >> (i * 8))));

        bool pulseVisible = (state.Flags & PulseVisibleFlag) != 0;
        if (satellite.pulse is not null)
            satellite.pulse.Visible = pulseVisible;
        if (satellite.pulseBloom is not null)
            satellite.pulseBloom.Visible = pulseVisible;
        if (satellite.computerScreen is not null)
            satellite.computerScreen.Visible = (state.Flags & ScreenVisibleFlag) != 0;
        if (satellite.computerScreenNoise is not null)
            satellite.computerScreenNoise.Visible = (state.Flags & ScreenNoiseVisibleFlag) != 0;
        if (satellite.computerScreenShine is not null)
            satellite.computerScreenShine.Visible = (state.Flags & ScreenShineVisibleFlag) != 0;
        if (satellite.screenBloom is not null)
        {
            satellite.screenBloom.Visible = (state.Flags & ScreenBloomVisibleFlag) != 0;
            satellite.screenBloom.Alpha = state.ScreenBloomAlpha;
        }
        if (satellite.pulseBloom is not null)
            satellite.pulseBloom.Alpha = state.PulseBloomAlpha;
        if (satellite.pulse is not null)
            satellite.pulse.Color = ColorFromPacked(state.PulseColor);
        if (satellite.computerScreen is not null)
            satellite.computerScreen.Color = ColorFromPacked(state.ScreenColor);

        HeartGem? heart = FindSatelliteHeart(level, satellite);
        bool gemPresent = (state.Flags & GemPresentFlag) != 0;
        if (gemPresent && heart is null && !level.Session.HeartGem)
        {
            heart = new HeartGem(state.HeartPosition) { Collidable = false };
            level.Add(heart);
        }
        else if (!gemPresent && heart is not null && !level.Session.HeartGem)
            heart.RemoveSelf();
        if (gemPresent && heart is not null)
        {
            heart.Position = state.HeartPosition;
            heart.Collidable = false;
        }
    }

    private static void ApplyBird(ForsakenCitySatellite.CodeBird bird, BirdState state)
    {
        BirdRemoteInfo applied = birdRemoteInfo.GetValue(bird, static _ => new());
        bool hard = WatchEntitySyncRegistry.IsApplyingLifecycleReset
            || !applied.HasState
            || Vector2.DistanceSquared(bird.Position, state.Position) >= 96f * 96f;
        if (hard)
        {
            bird.Position = state.Position;
            applied.Start = applied.Target = state.Position;
            applied.Elapsed = BirdAnchorInterval;
        }
        else
        {
            applied.Start = bird.Position;
            applied.Target = state.Position;
            applied.Elapsed = 0f;
        }
        applied.HasState = true;
        bird.speed = state.Speed;
        bird.timer = state.Timer;
        bird.Active = (state.Flags & BirdActiveFlag) != 0;
        bird.Visible = (state.Flags & BirdVisibleFlag) != 0;
        if (!applied.AllowRoutine)
            bird.routine.Active = false;
        if (bird.sprite is null)
            return;
        string? animation = DecodeBirdAnimation(state.Animation);
        if (animation is not null && bird.sprite.Has(animation))
        {
            if (bird.sprite.CurrentAnimationID != animation)
                bird.sprite.Play(animation, restart: true);
            if (bird.sprite.CurrentAnimationTotalFrames > 0)
                bird.sprite.SetAnimationFrame(Math.Min(
                    state.AnimationFrame,
                    bird.sprite.CurrentAnimationTotalFrames - 1
                ));
        }
        bird.sprite.Scale = state.Scale;
        bird.sprite.Rotation = state.Rotation;
        Color spriteColor = default;
        spriteColor.PackedValue = state.SpriteColor;
        bird.sprite.Color = spriteColor;
        bird.sprite.Visible = (state.Flags & BirdSpriteVisibleFlag) != 0;

        bool heartPresent = (state.Flags & BirdHeartPresentFlag) != 0;
        if (heartPresent && bird.heartImage is null)
        {
            bird.heartImage = new Image(GFX.Game["collectables/heartGem/shape"]);
            bird.heartImage.CenterOrigin();
            bird.Add(bird.heartImage);
        }
        else if (!heartPresent && bird.heartImage is not null)
        {
            bird.Remove(bird.heartImage);
            bird.heartImage = null!;
        }
        if (bird.heartImage is not null)
        {
            bird.heartImage.Scale = state.HeartScale;
            bird.heartImage.Visible = (state.Flags & BirdHeartVisibleFlag) != 0;
        }
    }

    private static HeartGem? FindSatelliteHeart(
        Level level,
        ForsakenCitySatellite satellite
    ) => WatchRoomEntityIndex.Enumerate<HeartGem>(level).FirstOrDefault(heart =>
        !heart.IsFake
        && (Vector2.DistanceSquared(heart.Position, satellite.gemSpawnPosition) <= 256f * 256f
            || Vector2.DistanceSquared(heart.Position, satellite.birdFlyPosition) <= 256f * 256f)
    );

    private static ForsakenCitySatellite? Find(Level level, int id)
        => WatchEntityIDTable<ForsakenCitySatellite>.Find(level, id);

    private static ForsakenCitySatellite? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "birdForsakenCityGem"
        );
        if (data is null)
            return null;
        ForsakenCitySatellite satellite = new(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<ForsakenCitySatellite>.Set(satellite, level.Session.Level, id);
        level.Add(satellite);
        return satellite;
    }

    private static void DisableDashListener(ForsakenCitySatellite satellite)
    {
        if (satellite.dashListener is null)
            return;
        satellite.Remove(satellite.dashListener);
        satellite.dashListener = null!;
    }

    private static byte EncodeDirection(string? direction) => direction switch
    {
        "U" => 1,
        "L" => 2,
        "DR" => 3,
        "UR" => 4,
        "UL" => 5,
        _ => 0,
    };

    private static (byte Count, ulong Values) PackInputs(IEnumerable<string> inputs)
    {
        byte count = 0;
        ulong values = 0;
        foreach (string input in inputs)
        {
            byte direction = EncodeDirection(input);
            if (direction == 0)
                continue;
            values |= (ulong)direction << (count * 8);
            if (++count == 6)
                break;
        }
        return (count, values);
    }

    private static string DecodeDirection(byte direction) => direction switch
    {
        1 => "U", 2 => "L", 3 => "DR", 4 => "UR", 5 => "UL", _ => string.Empty,
    };

    private static byte EncodeBirdAnimation(string? animation) => animation switch
    {
        "idle" => 0,
        "fly" => 1,
        "dash" => 2,
        "flyup" => 3,
        "flyupIdle" => 4,
        "flyupRoll" => 5,
        _ => byte.MaxValue,
    };

    private static string? DecodeBirdAnimation(byte animation) => animation switch
    {
        0 => "idle", 1 => "fly", 2 => "dash", 3 => "flyup", 4 => "flyupIdle",
        5 => "flyupRoll", _ => null,
    };

    private static void Satellite_ctor(
        On.Celeste.ForsakenCitySatellite.orig_ctor orig,
        ForsakenCitySatellite self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<ForsakenCitySatellite>.Set(self, data.Level.Name, data.ID);
    }

    private static void Satellite_Added(
        On.Celeste.ForsakenCitySatellite.orig_Added orig,
        ForsakenCitySatellite self,
        Scene scene
    )
    {
        orig(self, scene);
        RegisterBirds(self);
        if (MiaoNetModule.IsWatching)
            DisableLocalPresentation(self);
    }

    private static System.Collections.IEnumerator Satellite_UnlockGem(
        On.Celeste.ForsakenCitySatellite.orig_UnlockGem orig,
        ForsakenCitySatellite self
    )
    {
        if (MiaoNetModule.IsWatching)
            return EmptyRoutine();
        if (self.Scene is Level level
            && WatchEntityIDTable<ForsakenCitySatellite>.TryGet(
                self,
                level.Session.Level,
                out int id
            ))
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.ForsakenCitySatellite, id), UnlockEvent, []
            ));
        return orig(self);
    }

    private static void Satellite_Update(
        On.Celeste.ForsakenCitySatellite.orig_Update orig,
        ForsakenCitySatellite self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (!MiaoNetModule.IsWatchedPlayerPaused)
            self.Components.Update();
    }

    private static void CodeBird_Update(
        On.Celeste.ForsakenCitySatellite.CodeBird.orig_Update orig,
        Entity entity
    )
    {
        ForsakenCitySatellite.CodeBird self = (ForsakenCitySatellite.CodeBird)entity;
        if (!MiaoNetModule.IsWatching)
        {
            orig(entity);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;

        BirdRemoteInfo applied = birdRemoteInfo.GetValue(self, static _ => new());
        if (applied.AllowRoutine)
        {
            orig(entity);
            if (!self.routine.Active)
                applied.AllowRoutine = false;
        }
        else
        {
            self.routine.Active = false;
            self.timer += Engine.DeltaTime;
            if (self.sprite is not null)
                self.sprite.Y = MathF.Sin(self.timer * 2f);
            self.Components.Update();
        }
        if (applied.HasState && applied.Elapsed < BirdAnchorInterval)
        {
            applied.Elapsed = Math.Min(BirdAnchorInterval, applied.Elapsed + Engine.DeltaTime);
            self.Position = Vector2.Lerp(
                applied.Start,
                applied.Target,
                applied.Elapsed / BirdAnchorInterval
            );
        }
    }

    private static void CodeBird_Dash(
        On.Celeste.ForsakenCitySatellite.CodeBird.orig_Dash orig,
        Entity entity
    )
    {
        ForsakenCitySatellite.CodeBird self = (ForsakenCitySatellite.CodeBird)entity;
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        PublishBirdEvent(self, BirdDashEvent, []);
        orig(entity);
    }

    private static void CodeBird_Transform(
        On.Celeste.ForsakenCitySatellite.CodeBird.orig_Transform orig,
        Entity entity,
        float delay
    )
    {
        ForsakenCitySatellite.CodeBird self = (ForsakenCitySatellite.CodeBird)entity;
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        byte[] payload = new byte[4];
        WatchEntityPayloadCodec.WriteSingle(payload, 0, delay);
        PublishBirdEvent(self, BirdTransformEvent, payload);
        orig(entity, delay);
    }

    private static void RegisterBirds(ForsakenCitySatellite satellite)
    {
        if (satellite.Scene is not Level level
            || !WatchEntityIDTable<ForsakenCitySatellite>.TryGet(
                satellite,
                level.Session.Level,
                out int id
            ))
            return;
        foreach (ForsakenCitySatellite.CodeBird bird in satellite.birds)
        {
            ushort subID = EncodeDirection(bird.code);
            if (subID != 0)
                birdOwners.AddOrUpdate(bird, new BirdOwner
                {
                    ID = id,
                    SubID = subID,
                    Level = level.Session.Level,
                });
        }
    }

    private static void PublishBirdEvent(
        ForsakenCitySatellite.CodeBird bird,
        byte eventID,
        ReadOnlySpan<byte> payload
    )
    {
        if (WatchEntitySyncRegistry.IsApplyingRemoteState
            || bird.Scene is not Level level
            || !birdOwners.TryGetValue(bird, out BirdOwner? owner)
            || !StringComparer.Ordinal.Equals(owner.Level, level.Session.Level))
            return;
        WatchEntitySyncRegistry.PublishEvent(level, new(
            new WatchEntityKey(
                WatchEntityKind.ForsakenCitySatellite,
                owner.ID,
                owner.SubID
            ),
            eventID,
            payload.ToArray()
        ));
    }

    private static void DisableLocalPresentation(ForsakenCitySatellite satellite)
    {
        DisableDashListener(satellite);
        foreach (Coroutine coroutine in satellite.Components.GetAll<Coroutine>())
            coroutine.Active = false;
    }

    private static Color ColorFromPacked(uint packed)
    {
        Color color = default;
        color.PackedValue = packed;
        return color;
    }

    private static System.Collections.IEnumerator EmptyRoutine()
    {
        yield break;
    }
}

internal sealed class WatchReflectionHeartStatueAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 12;
    private static readonly Vector2 HeartOffset = new(0f, -52f);
    private const byte EnabledFlag = 1 << 0;
    private const byte GemPresentFlag = 1 << 1;
    private const byte ActivatingFlag = 1 << 2;
    private const byte TorchEvent = 1;
    private const byte ActivateEvent = 2;

    private readonly record struct StatueState(
        byte Flags,
        byte TorchMask,
        byte InputCount,
        ulong Inputs
    );

    private sealed class TorchOwner
    {
        public int ParentID { get; init; }
        public ushort SubID { get; init; }
    }

    private sealed class RemoteInfo
    {
        public bool ActivationStarted { get; set; }
    }

    private static readonly WatchReflectionHeartStatueAdapter instance = new();
    private static readonly ConditionalWeakTable<ReflectionHeartStatue.Torch, TorchOwner> owners = new();
    private static readonly ConditionalWeakTable<ReflectionHeartStatue, RemoteInfo> remoteInfo = new();
    public WatchEntityKind Kind => WatchEntityKind.ReflectionHeartStatue;

    public static void Load()
    {
        On.Celeste.ReflectionHeartStatue.ctor += Statue_ctor;
        On.Celeste.ReflectionHeartStatue.Added += Statue_Added;
        On.Celeste.ReflectionHeartStatue.Activate += Statue_Activate;
        On.Celeste.ReflectionHeartStatue.Torch.Activate += Torch_Activate;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.ReflectionHeartStatue.Torch.Activate -= Torch_Activate;
        On.Celeste.ReflectionHeartStatue.Activate -= Statue_Activate;
        On.Celeste.ReflectionHeartStatue.Added -= Statue_Added;
        On.Celeste.ReflectionHeartStatue.ctor -= Statue_ctor;
        WatchEntityIDTable<ReflectionHeartStatue>.Clear();
        owners.Clear();
        remoteInfo.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (ReflectionHeartStatue statue in WatchRoomEntityIndex.Enumerate<ReflectionHeartStatue>(level))
        {
            if (!WatchEntityIDTable<ReflectionHeartStatue>.TryGet(
                statue,
                level.Session.Level,
                out int id
            ))
                continue;
            Vector2 heartPosition = statue.Position + HeartOffset;
            HeartGem? heart = FindHeart(level, heartPosition);
            byte flags = 0;
            if (statue.enabled) flags |= EnabledFlag;
            if (heart is not null) flags |= GemPresentFlag;
            byte mask = 0;
            foreach (ReflectionHeartStatue.Torch torch in statue.torches)
            {
                if (torch.Index is >= 0 and < 4 && torch.Activated)
                    mask |= (byte)(1 << torch.Index);
            }
            if (!statue.enabled && heart is null && !level.Session.HeartGem && mask == 0b1111)
                flags |= ActivatingFlag;
            (byte inputCount, ulong acceptedInputs) = PackInputs(statue.currentInputs);
            StatueState current = new(flags, mask, inputCount, acceptedInputs);
            yield return WatchEntityState.FromTyped(
                new(Kind, id),
                current,
                PayloadSize,
                static (payload, state) =>
                {
                    payload[0] = state.Flags;
                    payload[1] = state.TorchMask;
                    payload[2] = state.InputCount;
                    for (int index = 0; index < state.InputCount; index++)
                        payload[4 + index] = (byte)(state.Inputs >> (index * 8));
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
        foreach (ReflectionHeartStatue statue in WatchRoomEntityIndex.Enumerate<ReflectionHeartStatue>(level))
        {
            if (!WatchEntityIDTable<ReflectionHeartStatue>.TryGet(
                statue,
                level.Session.Level,
                out int id
            ))
                continue;
            if (!desired.Remove(id, out ReadOnlyMemory<byte> memory))
            {
                if (isCompleteState)
                    statue.Visible = statue.Active = false;
                continue;
            }
            Apply(level, statue, memory.Span);
            changed = true;
        }
        foreach ((int id, ReadOnlyMemory<byte> memory) in desired)
        {
            ReflectionHeartStatue? statue = Recreate(level, id);
            if (statue is null)
                continue;
            Apply(level, statue, memory.Span);
            changed = true;
        }
        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        ReflectionHeartStatue? statue = Find(level, entityEvent.Key.EntityID);
        if (statue is null || entityEvent.Payload.Length != 0)
            return;
        if (entityEvent.EventID == TorchEvent && entityEvent.Key.SubID is >= 1 and <= 4)
        {
            ReflectionHeartStatue.Torch? torch = statue.torches.FirstOrDefault(candidate =>
                candidate.Index + 1 == entityEvent.Key.SubID
            );
            torch?.PlayLit();
        }
        else if (entityEvent.EventID == ActivateEvent && entityEvent.Key.SubID == 0)
            StartRemoteActivation(statue);
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.ReflectionHeartStatue
            || state.Key.SubID != 0 || payload.Length != PayloadSize
            || (payload[0] & ~0b0000_0111) != 0 || (payload[1] & ~0b0000_1111) != 0
            || payload[2] > 6 || payload[3] != 0 || payload[10] != 0 || payload[11] != 0)
            return false;
        return payload.Slice(4, payload[2]).ToArray().All(direction => direction is >= 1 and <= 5)
            && payload.Slice(4 + payload[2], 6 - payload[2]).ToArray().All(value => value == 0);
    }

    private static void Apply(Level level, ReflectionHeartStatue statue, ReadOnlySpan<byte> payload)
    {
        statue.Visible = statue.Active = true;
        statue.enabled = (payload[0] & EnabledFlag) != 0;
        DisableDashListener(statue);
        statue.currentInputs.Clear();
        for (int i = 0; i < payload[2]; i++)
            statue.currentInputs.Add(DecodeDirection(payload[4 + i]));
        byte mask = payload[1];
        foreach (ReflectionHeartStatue.Torch torch in statue.torches)
        {
            bool lit = torch.Index is >= 0 and < 4 && (mask & (1 << torch.Index)) != 0;
            if (lit)
                torch.PlayLit();
            else if (torch.sprite?.Has("idle") == true)
                torch.sprite.Play("idle");
        }
        Vector2 heartPosition = statue.Position + HeartOffset;
        HeartGem? heart = FindHeart(level, heartPosition);
        bool gemPresent = (payload[0] & GemPresentFlag) != 0;
        RemoteInfo applied = remoteInfo.GetValue(statue, static _ => new());
        if ((payload[0] & ActivatingFlag) != 0 && heart is null && !level.Session.HeartGem)
            StartRemoteActivation(statue);
        if (gemPresent && heart is null && !level.Session.HeartGem && !applied.ActivationStarted)
        {
            heart = new HeartGem(heartPosition) { Collidable = false };
            level.Add(heart);
        }
        else if (!gemPresent && heart is not null && !level.Session.HeartGem)
            heart.RemoveSelf();
        if (heart is not null)
            heart.Collidable = false;
        if (heart is not null || level.Session.HeartGem)
            applied.ActivationStarted = false;
    }

    private static void StartRemoteActivation(ReflectionHeartStatue statue)
    {
        RemoteInfo applied = remoteInfo.GetValue(statue, static _ => new());
        if (applied.ActivationStarted || statue.Scene is not Level level
            || level.Session.HeartGem
            || FindHeart(level, statue.Position + HeartOffset) is not null)
            return;
        applied.ActivationStarted = true;
        statue.Activate(false);
    }

    private static HeartGem? FindHeart(Level level, Vector2 position)
        => WatchRoomEntityIndex.Enumerate<HeartGem>(level).FirstOrDefault(heart =>
            !heart.IsFake && Vector2.DistanceSquared(heart.Position, position) <= 64f
        );

    private static ReflectionHeartStatue? Find(Level level, int id)
        => WatchEntityIDTable<ReflectionHeartStatue>.Find(level, id);

    private static ReflectionHeartStatue? Recreate(Level level, int id)
    {
        EntityData? data = level.Session.LevelData.Entities.FirstOrDefault(entity =>
            entity.ID == id && entity.Name == "reflectionHeartStatue"
        );
        if (data is null)
            return null;
        ReflectionHeartStatue statue = new(data, new(
            level.Session.LevelData.Bounds.Left,
            level.Session.LevelData.Bounds.Top
        ));
        WatchEntityIDTable<ReflectionHeartStatue>.Set(statue, level.Session.Level, id);
        level.Add(statue);
        return statue;
    }

    private static void RegisterTorches(ReflectionHeartStatue statue)
    {
        if (statue.Scene is not Level level
            || !WatchEntityIDTable<ReflectionHeartStatue>.TryGet(
                statue,
                level.Session.Level,
                out int id
            ))
            return;
        foreach (ReflectionHeartStatue.Torch torch in statue.torches)
            owners.AddOrUpdate(torch, new TorchOwner
            {
                ParentID = id,
                SubID = checked((ushort)(torch.Index + 1)),
            });
    }

    private static void DisableDashListener(ReflectionHeartStatue statue)
    {
        if (statue.dashListener is null)
            return;
        statue.Remove(statue.dashListener);
        statue.dashListener = null!;
    }

    private static byte EncodeDirection(string? direction) => direction switch
    {
        "U" => 1, "L" => 2, "DR" => 3, "UR" => 4, "UL" => 5, _ => 0,
    };

    private static (byte Count, ulong Values) PackInputs(IEnumerable<string> inputs)
    {
        byte count = 0;
        ulong values = 0;
        foreach (string input in inputs)
        {
            byte direction = EncodeDirection(input);
            if (direction == 0)
                continue;
            values |= (ulong)direction << (count * 8);
            if (++count == 6)
                break;
        }
        return (count, values);
    }

    private static string DecodeDirection(byte direction) => direction switch
    {
        1 => "U", 2 => "L", 3 => "DR", 4 => "UR", 5 => "UL", _ => string.Empty,
    };

    private static void Statue_ctor(
        On.Celeste.ReflectionHeartStatue.orig_ctor orig,
        ReflectionHeartStatue self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        WatchEntityIDTable<ReflectionHeartStatue>.Set(self, data.Level.Name, data.ID);
    }

    private static void Statue_Added(
        On.Celeste.ReflectionHeartStatue.orig_Added orig,
        ReflectionHeartStatue self,
        Scene scene
    )
    {
        orig(self, scene);
        RegisterTorches(self);
        if (MiaoNetModule.IsWatching)
            DisableDashListener(self);
    }

    private static void Statue_Activate(
        On.Celeste.ReflectionHeartStatue.orig_Activate orig,
        ReflectionHeartStatue self,
        bool firstLevelLoad
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        if (!MiaoNetModule.IsWatching && !firstLevelLoad && self.Scene is Level level
            && WatchEntityIDTable<ReflectionHeartStatue>.TryGet(
                self,
                level.Session.Level,
                out int id
            ))
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(WatchEntityKind.ReflectionHeartStatue, id), ActivateEvent, []
            ));
        orig(self, firstLevelLoad);
    }

    private static void Torch_Activate(
        On.Celeste.ReflectionHeartStatue.Torch.orig_Activate orig,
        ReflectionHeartStatue.Torch self
    )
    {
        if (MiaoNetModule.IsWatching && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;
        orig(self);
        if (!MiaoNetModule.IsWatching && self.Scene is Level level
            && owners.TryGetValue(self, out TorchOwner? owner))
            WatchEntitySyncRegistry.PublishEvent(level, new(
                new WatchEntityKey(
                    WatchEntityKind.ReflectionHeartStatue,
                    owner.ParentID,
                    owner.SubID
                ),
                TorchEvent,
                []
            ));
    }
}
