using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchStrawberrySeedAdapter : IWatchEntityAdapter
{
    private const int SeedPayloadSize = 10;
    private const byte ParentWaitingFlag = 1 << 0;
    private const byte ParentVisibleFlag = 1 << 1;
    private const byte ParentCollidableFlag = 1 << 2;
    private const byte ParentBloomVisibleFlag = 1 << 3;
    private const byte ParentLightVisibleFlag = 1 << 4;
    private const byte ParentKnownFlags = ParentWaitingFlag
        | ParentVisibleFlag
        | ParentCollidableFlag
        | ParentBloomVisibleFlag
        | ParentLightVisibleFlag;
    private const byte SeedVisibleFlag = 1 << 0;
    private const byte SeedCollidableFlag = 1 << 1;
    private const byte SeedGhostFlag = 1 << 2;
    private const byte SeedKnownFlags = SeedVisibleFlag | SeedCollidableFlag | SeedGhostFlag;

    private static readonly WatchStrawberrySeedAdapter instance = new();
    private static readonly Dictionary<WatchEntityKey, WatchEntityState> remoteStates = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.StrawberrySeed;

    public static void Load()
    {
        On.Celeste.Level.Update += Level_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Level.Update -= Level_Update;
        remoteStates.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        foreach (Strawberry strawberry in level.Entities.OfType<Strawberry>())
        {
            if (strawberry.ID.Level != room || strawberry.Seeds is not { Count: > 0 })
                continue;

            yield return EncodeParent(strawberry);
            foreach (StrawberrySeed seed in strawberry.Seeds)
            {
                if (seed.Scene == level && seed.index >= 0 && seed.index < ushort.MaxValue)
                    yield return EncodeSeed(strawberry.ID.ID, seed);
            }
        }
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

        foreach (WatchEntityState state in states)
        {
            if (!IsValidState(state) || !remoteStates.TryAdd(state.Key, state))
            {
                if (!isCompleteState && IsValidState(state))
                    remoteStates[state.Key] = state;
                else
                    return WatchEntityApplyResult.None;
            }
        }

        return ApplyRemoteStates(level, requireCompleteCoverage: isCompleteState);
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
    }

    private static WatchEntityApplyResult ApplyRemoteStates(Level level, bool requireCompleteCoverage)
    {
        bool changed = false;
        bool requiresReload = false;
        string room = level.Session.Level;
        Dictionary<int, Strawberry> strawberries = level.Entities
            .OfType<Strawberry>()
            .Where(strawberry => strawberry.ID.Level == room && strawberry.Seeds is { Count: > 0 })
            .ToDictionary(strawberry => strawberry.ID.ID);
        HashSet<WatchEntityKey> foundSeedKeys = new();

        foreach ((int id, Strawberry strawberry) in strawberries)
        {
            WatchEntityKey parentKey = new(WatchEntityKind.StrawberrySeed, id);
            if (!remoteStates.TryGetValue(parentKey, out WatchEntityState parentState))
            {
                requiresReload |= requireCompleteCoverage;
                continue;
            }

            byte parentFlags = parentState.Payload.Span[0];
            bool waiting = (parentFlags & ParentWaitingFlag) != 0;
            bool visible = (parentFlags & ParentVisibleFlag) != 0;
            bool collidable = (parentFlags & ParentCollidableFlag) != 0;
            bool bloomVisible = (parentFlags & ParentBloomVisibleFlag) != 0;
            bool lightVisible = (parentFlags & ParentLightVisibleFlag) != 0;
            if (strawberry.WaitingOnSeeds != waiting
                || strawberry.Visible != visible
                || strawberry.Collidable != collidable
                || strawberry.bloom.Visible != bloomVisible
                || strawberry.light.Visible != lightVisible)
            {
                strawberry.WaitingOnSeeds = waiting;
                strawberry.Visible = visible;
                strawberry.Collidable = collidable;
                strawberry.bloom.Visible = bloomVisible;
                strawberry.light.Visible = lightVisible;
                changed = true;
            }
        }

        foreach (StrawberrySeed seed in level.Entities.OfType<StrawberrySeed>().ToArray())
        {
            if (seed.index < 0 || seed.index >= ushort.MaxValue)
                continue;

            int strawberryID = seed.Strawberry.ID.ID;
            WatchEntityKey seedKey = new(
                KindValue,
                strawberryID,
                checked((ushort)(seed.index + 1))
            );
            foundSeedKeys.Add(seedKey);
            if (!remoteStates.TryGetValue(seedKey, out WatchEntityState seedState))
            {
                if (requireCompleteCoverage)
                {
                    seed.RemoveSelf();
                    changed = true;
                }
                continue;
            }

            ReadOnlySpan<byte> payload = seedState.Payload.Span;
            WatchStrawberrySeedPhase phase = (WatchStrawberrySeedPhase)payload[0];
            byte flags = payload[1];
            Vector2 position = new(
                WatchEntityPayloadCodec.ReadSingle(payload, 2),
                WatchEntityPayloadCodec.ReadSingle(payload, 6)
            );
            bool seedVisible = phase != WatchStrawberrySeedPhase.Following
                && (flags & SeedVisibleFlag) != 0;
            bool seedCollidable = phase == WatchStrawberrySeedPhase.Ready
                && (flags & SeedCollidableFlag) != 0;
            bool ghost = (flags & SeedGhostFlag) != 0;
            bool finished = phase == WatchStrawberrySeedPhase.Combining;
            bool losing = phase == WatchStrawberrySeedPhase.Returning;
            if (seed.finished && !finished)
            {
                requiresReload = true;
                continue;
            }

            if (seed.Position != position
                || seed.Visible != seedVisible
                || seed.Collidable != seedCollidable
                || seed.ghost != ghost
                || seed.finished != finished
                || seed.losing != losing)
            {
                seed.Position = position;
                seed.Visible = seedVisible;
                seed.Collidable = seedCollidable;
                if (seed.ghost != ghost)
                    ReplaceSeedSprite(level, seed, ghost);
                seed.finished = finished;
                seed.losing = losing;
                if (finished)
                {
                    seed.Depth = -2000002;
                    seed.Tag = Tags.FrozenUpdate;
                }
                changed = true;
            }
        }

        if (remoteStates.Keys.Any(key => key.SubID == 0 && !strawberries.ContainsKey(key.EntityID)))
            requiresReload = true;
        if (remoteStates.Keys.Any(key => key.SubID != 0 && !foundSeedKeys.Contains(key)))
            requiresReload = true;

        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    private static WatchEntityState EncodeParent(Strawberry strawberry)
    {
        byte flags = 0;
        if (strawberry.WaitingOnSeeds)
            flags |= ParentWaitingFlag;
        if (strawberry.Visible)
            flags |= ParentVisibleFlag;
        if (strawberry.Collidable)
            flags |= ParentCollidableFlag;
        if (strawberry.bloom.Visible)
            flags |= ParentBloomVisibleFlag;
        if (strawberry.light.Visible)
            flags |= ParentLightVisibleFlag;
        return new(new WatchEntityKey(KindValue, strawberry.ID.ID), [flags]);
    }

    private static WatchEntityState EncodeSeed(int strawberryID, StrawberrySeed seed)
    {
        WatchStrawberrySeedPhase phase = seed.finished
            ? WatchStrawberrySeedPhase.Combining
            : seed.follower.Leader is not null
                ? WatchStrawberrySeedPhase.Following
                : seed.losing
                    ? WatchStrawberrySeedPhase.Returning
                    : WatchStrawberrySeedPhase.Ready;
        byte flags = 0;
        if (seed.Visible)
            flags |= SeedVisibleFlag;
        if (seed.Collidable)
            flags |= SeedCollidableFlag;
        if (seed.ghost)
            flags |= SeedGhostFlag;
        byte[] payload = new byte[SeedPayloadSize];
        payload[0] = (byte)phase;
        payload[1] = flags;
        WatchEntityPayloadCodec.WriteSingle(payload, 2, seed.Position.X);
        WatchEntityPayloadCodec.WriteSingle(payload, 6, seed.Position.Y);
        return new(
            new WatchEntityKey(KindValue, strawberryID, checked((ushort)(seed.index + 1))),
            payload
        );
    }

    private static bool IsValidState(WatchEntityState state)
    {
        if (state.Key.Kind != KindValue)
            return false;
        ReadOnlySpan<byte> payload = state.Payload.Span;
        if (state.Key.SubID == 0)
            return payload.Length == 1 && (payload[0] & ~ParentKnownFlags) == 0;
        return payload.Length == SeedPayloadSize
            && payload[0] <= (byte)WatchStrawberrySeedPhase.Combining
            && (payload[1] & ~SeedKnownFlags) == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 2))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 6));
    }

    private static void ReplaceSeedSprite(Level level, StrawberrySeed seed, bool ghost)
    {
        Sprite previous = seed.sprite;
        string spriteID = ghost
            ? "ghostberrySeed"
            : level.Session.Area.Mode == AreaMode.CSide
                ? "goldberrySeed"
                : "strawberrySeed";
        Sprite replacement = GFX.SpriteBank.Create(spriteID);
        replacement.Position = previous.Position;
        replacement.Scale = previous.Scale;
        replacement.Rotation = previous.Rotation;
        replacement.Visible = previous.Visible;
        replacement.Color = ghost ? Color.White * 0.8f : Color.White;
        replacement.OnFrameChange = previous.OnFrameChange;
        string animation = previous.CurrentAnimationID;
        int frame = previous.CurrentAnimationFrame;
        if (!string.IsNullOrEmpty(animation) && replacement.Has(animation))
        {
            replacement.Play(animation);
            replacement.SetAnimationFrame(frame);
            replacement.Rate = previous.Rate;
        }
        else if (replacement.Has("idle"))
        {
            replacement.Play("idle");
        }

        seed.Remove(previous);
        seed.ghost = ghost;
        seed.sprite = replacement;
        seed.Add(replacement);
        StrawberrySeed.P_Burst.Color = replacement.Color;
    }

    private static void Level_Update(On.Celeste.Level.orig_Update orig, Level self)
    {
        orig(self);
        if (MiaoNetModule.IsWatching && StringComparer.Ordinal.Equals(remoteRoom, self.Session.Level))
            ApplyRemoteStates(self, requireCompleteCoverage: false);
    }

    private const WatchEntityKind KindValue = WatchEntityKind.StrawberrySeed;
}
