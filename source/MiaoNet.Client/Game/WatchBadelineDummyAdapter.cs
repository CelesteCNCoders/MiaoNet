using MiaoNet.Shared;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Synchronizes the presentation-only Badeline actors spawned by boost and
/// ascent coroutines. The Watcher never runs those gameplay/cutscene routines;
/// these remote-owned dummies have no authority over Player, Camera or Session.
/// </summary>
internal sealed class WatchBadelineDummyAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 40;
    private const byte VisibleFlag = 1 << 0;
    private const byte SpriteVisibleFlag = 1 << 1;
    private const byte HairVisibleFlag = 1 << 2;
    private const byte LightVisibleFlag = 1 << 3;
    private const byte FacingLeftFlag = 1 << 4;

    private readonly record struct DummyState(
        byte Flags,
        byte AnimationFrame,
        ushort Animation,
        int Depth,
        Vector2 Position,
        Vector2 Scale,
        float Rotation,
        float Rate,
        float HairAlpha,
        float LightAlpha
    );

    private sealed class SourceIdentity
    {
        public int ID { get; }
        public SourceIdentity(int id) => ID = id;
    }

    private sealed class RemoteInfo
    {
        public Level? Owner { get; set; }
        public WatchRemotePosition Position { get; } = new();
    }

    private static readonly WatchBadelineDummyAdapter instance = new();
    private static readonly ConditionalWeakTable<BadelineDummy, SourceIdentity> sourceIDs = new();
    private static readonly ConditionalWeakTable<BadelineDummy, WatchTimedStateCache> sync = new();
    private static readonly ConditionalWeakTable<BadelineDummy, RemoteInfo> remote = new();
    private static readonly Dictionary<int, BadelineDummy> remoteByID = new();
    private static int nextSourceID;

    public WatchEntityKind Kind => WatchEntityKind.BadelineDummy;

    public static void Load()
    {
        On.Celeste.BadelineDummy.Update += BadelineDummy_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.BadelineDummy.Update -= BadelineDummy_Update;
        sourceIDs.Clear();
        sync.Clear();
        remote.Clear();
        remoteByID.Clear();
        nextSourceID = 0;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        foreach (BadelineDummy dummy in WatchRoomEntityIndex.Enumerate<BadelineDummy>(level))
        {
            if (remote.TryGetValue(dummy, out _))
                continue;
            SourceIdentity identity = sourceIDs.GetValue(dummy,
                static _ => new SourceIdentity(Interlocked.Increment(ref nextSourceID)));
            DummyState current = Capture(dummy);
            yield return sync.GetValue(dummy, static _ => new()).Capture(
                new(Kind, identity.ID), current, current.Flags, PayloadSize, Encode,
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
        Dictionary<int, ReadOnlyMemory<byte>> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state.Payload))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        foreach ((int id, ReadOnlyMemory<byte> memory) in desired)
        {
            ReadOnlySpan<byte> payload = memory.Span;
            bool hasRemote = remoteByID.TryGetValue(id, out BadelineDummy? dummy)
                && remote.TryGetValue(dummy, out RemoteInfo? existingInfo)
                && ReferenceEquals(existingInfo.Owner, level);
            if (!hasRemote)
            {
                if (dummy is not null)
                    RemoveRemote(id, dummy);
                dummy = new BadelineDummy(ReadPosition(payload));
                dummy.AutoAnimator.Active = false;
                remote.Add(dummy, new RemoteInfo { Owner = level });
                remoteByID[id] = dummy;
                level.Add(dummy);
                changed = true;
            }
            else if (dummy!.Scene is null && !level.Entities.ToAdd.Contains(dummy))
                level.Add(dummy);

            Apply(dummy!, payload);
            changed = true;
        }

        if (isCompleteState)
        {
            foreach ((int id, BadelineDummy dummy) in remoteByID.ToArray())
            {
                if (!desired.ContainsKey(id))
                {
                    RemoveRemote(id, dummy);
                    changed = true;
                }
            }
        }

        return changed ? WatchEntityApplyResult.SceneChanged : WatchEntityApplyResult.None;
    }


    private static DummyState Capture(BadelineDummy dummy)
    {
        byte flags = 0;
        if (dummy.Visible) flags |= VisibleFlag;
        if (dummy.Sprite.Visible) flags |= SpriteVisibleFlag;
        if (dummy.Hair.Visible) flags |= HairVisibleFlag;
        if (dummy.Light.Visible) flags |= LightVisibleFlag;
        if (dummy.Hair.Facing == Facings.Left) flags |= FacingLeftFlag;
        return new(
            flags,
            (byte)Math.Clamp(dummy.Sprite.CurrentAnimationFrame, 0, byte.MaxValue),
            WatchSpriteState.EncodeAnimation(dummy.Sprite),
            dummy.Depth,
            dummy.Position,
            dummy.Sprite.Scale,
            dummy.Sprite.Rotation,
            dummy.Sprite.Rate,
            Math.Clamp(dummy.Hair.Alpha, 0f, 1f),
            Math.Clamp(dummy.Light.Alpha, 0f, 1f)
        );
    }

    private static void Encode(Span<byte> payload, DummyState state)
    {
        payload[0] = state.Flags;
        payload[1] = state.AnimationFrame;
        WatchEntityPayloadCodec.WriteUInt16(payload, 2, state.Animation);
        WatchEntityPayloadCodec.WriteInt32(payload, 4, state.Depth);
        WatchEntityPayloadCodec.WriteVector2(payload, 8, state.Position);
        WatchEntityPayloadCodec.WriteVector2(payload, 16, state.Scale);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.Rotation);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.Rate);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.HairAlpha);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.LightAlpha);
    }

    private static void Apply(BadelineDummy dummy, ReadOnlySpan<byte> payload)
    {
        RemoteInfo info = remote.GetValue(dummy, static _ => new());
        info.Position.Apply(dummy, ReadPosition(payload));
        dummy.Visible = (payload[0] & VisibleFlag) != 0;
        dummy.Sprite.Visible = (payload[0] & SpriteVisibleFlag) != 0;
        dummy.Hair.Visible = (payload[0] & HairVisibleFlag) != 0;
        dummy.Light.Visible = (payload[0] & LightVisibleFlag) != 0;
        dummy.Hair.Facing = (payload[0] & FacingLeftFlag) != 0 ? Facings.Left : Facings.Right;
        dummy.Depth = WatchEntityPayloadCodec.ReadInt32(payload, 4);
        dummy.Sprite.Scale = WatchEntityPayloadCodec.ReadVector2(payload, 16);
        dummy.Sprite.Rotation = WatchEntityPayloadCodec.ReadSingle(payload, 24);
        dummy.Sprite.Rate = WatchEntityPayloadCodec.ReadSingle(payload, 28);
        dummy.Hair.Alpha = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(payload, 32), 0f, 1f);
        dummy.Light.Alpha = Math.Clamp(WatchEntityPayloadCodec.ReadSingle(payload, 36), 0f, 1f);
        dummy.AutoAnimator.Active = false;
        WatchSpriteState.ApplyAnimation(
            dummy.Sprite,
            WatchEntityPayloadCodec.ReadUInt16(payload, 2),
            payload[1]
        );
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.BadelineDummy
            && state.Key.SubID == 0
            && payload.Length == PayloadSize
            && (payload[0] & ~0b0001_1111) == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 12))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 16))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 20))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 24))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 28))
            && WatchEntityPayloadCodec.ReadSingle(payload, 32) is >= 0f and <= 1f
            && WatchEntityPayloadCodec.ReadSingle(payload, 36) is >= 0f and <= 1f;
    }

    private static Vector2 ReadPosition(ReadOnlySpan<byte> payload)
        => WatchEntityPayloadCodec.ReadVector2(payload, 8);

    private static void RemoveRemote(int id, BadelineDummy dummy)
    {
        remoteByID.Remove(id);
        dummy.RemoveSelf();
    }

    private static void BadelineDummy_Update(
        On.Celeste.BadelineDummy.orig_Update orig,
        BadelineDummy self
    )
    {
        if (!MiaoNetModule.IsWatching || !remote.TryGetValue(self, out RemoteInfo? info))
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        info.Position.Update(self);
        self.Components.Update();
        self.AutoAnimator.Active = false;
    }
}
