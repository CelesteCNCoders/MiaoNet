using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchLavaAdapter : IWatchEntityAdapter
{
    private const int PayloadSize = 40;
    private const ushort RisingSubID = 0;
    private const ushort SandwichSubID = 1;
    private const byte VisibleFlag = 1 << 0;
    private const byte CollidableFlag = 1 << 1;
    private const byte IceModeFlag = 1 << 2;
    private const byte WaitingFlag = 1 << 3;
    private const byte LeavingFlag = 1 << 4;
    private const byte SpecialFlag = 1 << 5;

    private readonly record struct LavaState(
        byte Flags,
        Vector2 EntityPosition,
        Vector2 BottomPosition,
        Vector2 TopPosition,
        float Lerp,
        float Delay,
        float TransitionStartY
    );

    private static readonly WatchLavaAdapter instance = new();
    private static readonly Dictionary<ushort, LavaState> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.Lava;

    public static void Load()
    {
        On.Celeste.RisingLava.OnPlayer += RisingLava_OnPlayer;
        On.Celeste.RisingLava.Update += RisingLava_Update;
        On.Celeste.SandwichLava.OnPlayer += SandwichLava_OnPlayer;
        On.Celeste.SandwichLava.Update += SandwichLava_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.SandwichLava.Update -= SandwichLava_Update;
        On.Celeste.SandwichLava.OnPlayer -= SandwichLava_OnPlayer;
        On.Celeste.RisingLava.Update -= RisingLava_Update;
        On.Celeste.RisingLava.OnPlayer -= RisingLava_OnPlayer;
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        RisingLava? rising = level.Entities.OfType<RisingLava>().FirstOrDefault();
        if (rising is not null)
            yield return Encode(RisingSubID, Capture(rising));

        SandwichLava? sandwich = level.Entities.OfType<SandwichLava>().FirstOrDefault();
        if (sandwich is not null)
            yield return Encode(SandwichSubID, Capture(sandwich));
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        string room = level.Session.Level;
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteStates.Clear();
            remoteRoom = room;
        }

        HashSet<ushort> packetTypes = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryDecode(state, out LavaState desired)
                || !packetTypes.Add(state.Key.SubID))
                return WatchEntityApplyResult.None;
            remoteStates[state.Key.SubID] = desired;
        }

        bool changed = false;
        bool requiresReload = false;
        RisingLava? rising = level.Entities.OfType<RisingLava>().FirstOrDefault();
        SandwichLava? sandwich = level.Entities.OfType<SandwichLava>().FirstOrDefault();
        if (remoteStates.TryGetValue(RisingSubID, out LavaState risingState))
        {
            if (rising is null)
                requiresReload = true;
            else
                changed |= Apply(rising, risingState);
        }
        else if (isCompleteState && rising is not null)
            requiresReload = true;

        if (remoteStates.TryGetValue(SandwichSubID, out LavaState sandwichState))
        {
            if (sandwich is null)
                requiresReload = true;
            else
                changed |= Apply(sandwich, sandwichState);
        }
        else if (isCompleteState && sandwich is not null)
            requiresReload = true;

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }

    private static LavaState Capture(RisingLava lava)
    {
        byte flags = CommonFlags(lava, lava.iceMode, lava.waiting);
        if (lava.intro)
            flags |= SpecialFlag;
        return new(
            flags,
            lava.Position,
            lava.bottomRect.Position,
            Vector2.Zero,
            lava.lerp,
            lava.delay,
            0f
        );
    }

    private static LavaState Capture(SandwichLava lava)
    {
        byte flags = CommonFlags(lava, lava.iceMode, lava.Waiting);
        if (lava.leaving)
            flags |= LeavingFlag;
        if (lava.persistent)
            flags |= SpecialFlag;
        return new(
            flags,
            lava.Position,
            lava.bottomRect.Position,
            lava.topRect.Position,
            lava.lerp,
            lava.delay,
            lava.transitionStartY
        );
    }

    private static byte CommonFlags(Entity lava, bool iceMode, bool waiting)
    {
        byte flags = 0;
        if (lava.Visible)
            flags |= VisibleFlag;
        if (lava.Collidable)
            flags |= CollidableFlag;
        if (iceMode)
            flags |= IceModeFlag;
        if (waiting)
            flags |= WaitingFlag;
        return flags;
    }

    private static WatchEntityState Encode(ushort subID, LavaState state)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = state.Flags;
        WatchEntityPayloadCodec.WriteSingle(payload, 4, state.EntityPosition.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 8, state.EntityPosition.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 12, state.BottomPosition.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 16, state.BottomPosition.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 20, state.TopPosition.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 24, state.TopPosition.Y);
        WatchEntityPayloadCodec.WriteSingle(payload, 28, state.Lerp);
        WatchEntityPayloadCodec.WriteSingle(payload, 32, state.Delay);
        WatchEntityPayloadCodec.WriteSingle(payload, 36, state.TransitionStartY);
        return new(new WatchEntityKey(WatchEntityKind.Lava, 0, subID), payload);
    }

    private static bool TryDecode(WatchEntityState state, out LavaState desired)
    {
        desired = default;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.Kind != WatchEntityKind.Lava
            || state.Key.EntityID != 0
            || state.Key.SubID > SandwichSubID
            || payload.Length != PayloadSize
            || (payload[0] & ~(VisibleFlag | CollidableFlag | IceModeFlag | WaitingFlag | LeavingFlag | SpecialFlag)) != 0
            || payload[1] != 0 || payload[2] != 0 || payload[3] != 0)
            return false;
        Vector2 entityPosition = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 4),
            WatchEntityPayloadCodec.ReadSingle(payload, 8)
        );
        Vector2 bottom = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 12),
            WatchEntityPayloadCodec.ReadSingle(payload, 16)
        );
        Vector2 top = new(
            WatchEntityPayloadCodec.ReadSingle(payload, 20),
            WatchEntityPayloadCodec.ReadSingle(payload, 24)
        );
        float lerp = WatchEntityPayloadCodec.ReadSingle(payload, 28);
        float delay = WatchEntityPayloadCodec.ReadSingle(payload, 32);
        float transitionStartY = WatchEntityPayloadCodec.ReadSingle(payload, 36);
        if (!float.IsFinite(entityPosition.X) || !float.IsFinite(entityPosition.Y)
            || !float.IsFinite(bottom.X) || !float.IsFinite(bottom.Y)
            || !float.IsFinite(top.X) || !float.IsFinite(top.Y)
            || !float.IsFinite(lerp) || !float.IsFinite(delay) || !float.IsFinite(transitionStartY))
            return false;
        if (state.Key.SubID == RisingSubID
            && (((payload[0] & LeavingFlag) != 0) || top != Vector2.Zero || transitionStartY != 0f))
            return false;
        desired = new(payload[0], entityPosition, bottom, top, lerp, delay, transitionStartY);
        return true;
    }

    private static bool Apply(RisingLava lava, LavaState desired)
    {
        bool visible = (desired.Flags & VisibleFlag) != 0;
        bool collidable = (desired.Flags & CollidableFlag) != 0;
        bool iceMode = (desired.Flags & IceModeFlag) != 0;
        bool waiting = (desired.Flags & WaitingFlag) != 0;
        bool intro = (desired.Flags & SpecialFlag) != 0;
        float lerp = iceMode
            ? Math.Max(lava.lerp, desired.Lerp)
            : Math.Min(lava.lerp, desired.Lerp);
        lerp = MathHelper.Clamp(lerp, 0f, 1f);
        bool changed = lava.Visible != visible
            || lava.Collidable != collidable
            || lava.Position != desired.EntityPosition
            || lava.iceMode != iceMode
            || lava.waiting != waiting
            || lava.intro != intro
            || lava.bottomRect.Position != desired.BottomPosition
            || lava.lerp != lerp
            || lava.delay != desired.Delay;
        lava.Visible = visible;
        lava.Collidable = collidable;
        lava.Position = desired.EntityPosition;
        lava.iceMode = iceMode;
        lava.waiting = waiting;
        lava.intro = intro;
        if (lava.bottomRect.Position != desired.BottomPosition)
            lava.bottomRect.dirty = true;
        lava.bottomRect.Position = desired.BottomPosition;
        lava.lerp = lerp;
        lava.delay = desired.Delay;
        UpdateVisuals(lava.bottomRect, iceMode, lerp);
        return changed;
    }

    private static bool Apply(SandwichLava lava, LavaState desired)
    {
        bool visible = (desired.Flags & VisibleFlag) != 0;
        bool collidable = (desired.Flags & CollidableFlag) != 0;
        bool iceMode = (desired.Flags & IceModeFlag) != 0;
        bool waiting = (desired.Flags & WaitingFlag) != 0;
        bool leaving = (desired.Flags & LeavingFlag) != 0;
        bool persistent = (desired.Flags & SpecialFlag) != 0;
        float lerp = iceMode
            ? Math.Max(lava.lerp, desired.Lerp)
            : Math.Min(lava.lerp, desired.Lerp);
        lerp = MathHelper.Clamp(lerp, 0f, 1f);
        bool changed = lava.Visible != visible
            || lava.Collidable != collidable
            || lava.Position != desired.EntityPosition
            || lava.iceMode != iceMode
            || lava.Waiting != waiting
            || lava.leaving != leaving
            || lava.persistent != persistent
            || lava.bottomRect.Position != desired.BottomPosition
            || lava.topRect.Position != desired.TopPosition
            || lava.lerp != lerp
            || lava.delay != desired.Delay
            || lava.transitionStartY != desired.TransitionStartY;
        lava.Visible = visible;
        lava.Collidable = collidable;
        lava.Position = desired.EntityPosition;
        lava.iceMode = iceMode;
        lava.Waiting = waiting;
        lava.leaving = leaving;
        lava.persistent = persistent;
        if (lava.bottomRect.Position != desired.BottomPosition)
            lava.bottomRect.dirty = true;
        if (lava.topRect.Position != desired.TopPosition)
            lava.topRect.dirty = true;
        lava.bottomRect.Position = desired.BottomPosition;
        lava.topRect.Position = desired.TopPosition;
        lava.lerp = lerp;
        lava.delay = desired.Delay;
        lava.transitionStartY = desired.TransitionStartY;
        UpdateVisuals(lava.bottomRect, iceMode, lerp);
        CopyVisuals(lava.bottomRect, lava.topRect);
        return changed;
    }

    private static void UpdateVisuals(LavaRect rect, bool iceMode, float lerp)
    {
        Color surfaceColor = Color.Lerp(RisingLava.Hot[0], RisingLava.Cold[0], lerp);
        Color edgeColor = Color.Lerp(RisingLava.Hot[1], RisingLava.Cold[1], lerp);
        Color centerColor = Color.Lerp(RisingLava.Hot[2], RisingLava.Cold[2], lerp);
        float spikey = lerp * 5f;
        float updateMultiplier = (1f - lerp) * 2f;
        float fade = iceMode ? 128f : 32f;
        if (rect.SurfaceColor != surfaceColor
            || rect.EdgeColor != edgeColor
            || rect.CenterColor != centerColor
            || rect.Spikey != spikey
            || rect.UpdateMultiplier != updateMultiplier
            || rect.Fade != fade)
            rect.dirty = true;
        rect.SurfaceColor = surfaceColor;
        rect.EdgeColor = edgeColor;
        rect.CenterColor = centerColor;
        rect.Spikey = spikey;
        rect.UpdateMultiplier = updateMultiplier;
        rect.Fade = fade;
    }

    private static void CopyVisuals(LavaRect source, LavaRect destination)
    {
        if (destination.SurfaceColor != source.SurfaceColor
            || destination.EdgeColor != source.EdgeColor
            || destination.CenterColor != source.CenterColor
            || destination.Spikey != source.Spikey
            || destination.UpdateMultiplier != source.UpdateMultiplier
            || destination.Fade != source.Fade)
            destination.dirty = true;
        destination.SurfaceColor = source.SurfaceColor;
        destination.EdgeColor = source.EdgeColor;
        destination.CenterColor = source.CenterColor;
        destination.Spikey = source.Spikey;
        destination.UpdateMultiplier = source.UpdateMultiplier;
        destination.Fade = source.Fade;
    }

    private static void RisingLava_OnPlayer(
        On.Celeste.RisingLava.orig_OnPlayer orig,
        RisingLava self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void SandwichLava_OnPlayer(
        On.Celeste.SandwichLava.orig_OnPlayer orig,
        SandwichLava self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void RisingLava_Update(
        On.Celeste.RisingLava.orig_Update orig,
        RisingLava self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.Components.Update();
        self.lerp = Calc.Approach(
            self.lerp,
            self.iceMode ? 1f : 0f,
            Engine.DeltaTime * 4f
        );
        UpdateVisuals(self.bottomRect, self.iceMode, self.lerp);
    }

    private static void SandwichLava_Update(
        On.Celeste.SandwichLava.orig_Update orig,
        SandwichLava self
    )
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }
        if (MiaoNetModule.IsWatchedPlayerPaused)
            return;
        self.Components.Update();
        self.lerp = Calc.Approach(
            self.lerp,
            self.iceMode ? 1f : 0f,
            Engine.DeltaTime * 4f
        );
        UpdateVisuals(self.bottomRect, self.iceMode, self.lerp);
        CopyVisuals(self.bottomRect, self.topRect);
    }
}
