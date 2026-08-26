using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchPersistentSessionBaseline
{
    private readonly HashSet<EntityID> doNotLoad;
    private readonly HashSet<EntityID> strawberries;
    private readonly bool cassette;
    private readonly bool heartGem;
    private readonly bool hitCheckpoint;
    private readonly string room;
    private readonly Vector2? respawnPoint;
    private readonly bool[] summitGems;
    private readonly Session.CoreModes coreMode;

    private WatchPersistentSessionBaseline(Session session)
    {
        doNotLoad = new(session.DoNotLoad);
        strawberries = new(session.Strawberries);
        cassette = session.Cassette;
        heartGem = session.HeartGem;
        hitCheckpoint = session.HitCheckpoint;
        room = session.Level;
        respawnPoint = session.RespawnPoint;
        summitGems = session.SummitGems.ToArray();
        coreMode = session.CoreMode;
    }

    public static WatchPersistentSessionBaseline Capture(Session session)
        => new(session);

    public void Restore(Session session)
    {
        session.DoNotLoad.Clear();
        session.DoNotLoad.UnionWith(doNotLoad);
        session.Strawberries.Clear();
        session.Strawberries.UnionWith(strawberries);
        session.Cassette = cassette;
        session.HeartGem = heartGem;
        session.HitCheckpoint = hitCheckpoint;
        if (session.Level == room)
            session.RespawnPoint = respawnPoint;
        session.SummitGems = summitGems.ToArray();
        session.CoreMode = coreMode;
    }
}

internal sealed class WatchPersistentSessionAdapter : IWatchEntityAdapter
{
    private static readonly WatchPersistentSessionAdapter instance = new();
    private static readonly WatchEntityKey StateKey = new(WatchEntityKind.PersistentSession, 0);

    public WatchEntityKind Kind => WatchEntityKind.PersistentSession;

    private WatchPersistentSessionAdapter()
    {
    }

    public static void Load()
        => WatchEntitySyncRegistry.Register(instance);

    public static void Unload()
        => WatchEntitySyncRegistry.Unregister(instance);

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        Session session = level.Session;
        string room = session.Level;
        HashSet<int> doNotLoadIDs = session.DoNotLoad
            .Where(id => id.Level == room)
            .Select(id => id.ID)
            .ToHashSet();

        Player? player = level.Tracker.GetEntity<Player>();
        if (player is not null)
        {
            foreach (Follower follower in player.Leader.Followers)
            {
                EntityID? id = follower.Entity switch
                {
                    Strawberry strawberry => strawberry.ID,
                    Key key => key.ID,
                    _ => null,
                };
                if (id is EntityID entityID && entityID.Level == room)
                    doNotLoadIDs.Add(entityID.ID);
            }
        }

        WatchPersistentSceneFlags flags = WatchPersistentSceneFlags.None;
        if (session.Cassette)
            flags |= WatchPersistentSceneFlags.Cassette;
        if (session.HeartGem)
            flags |= WatchPersistentSceneFlags.HeartGem;
        if (session.GetFlag(HeartGem.FAKE_HEART_FLAG))
            flags |= WatchPersistentSceneFlags.FakeHeart;
        if (session.HitCheckpoint)
            flags |= WatchPersistentSceneFlags.HitCheckpoint;
        if (session.RespawnPoint.HasValue)
            flags |= WatchPersistentSceneFlags.HasRespawnPoint;
        Cassette? cassette = level.Entities.OfType<Cassette>().FirstOrDefault();
        if (cassette?.IsGhost == true)
            flags |= WatchPersistentSceneFlags.CassetteGhost;
        HeartGem? heartGem = level.Entities
            .OfType<HeartGem>()
            .FirstOrDefault(heart => !heart.IsFake);
        if (heartGem?.IsGhost == true)
            flags |= WatchPersistentSceneFlags.HeartGemGhost;

        byte summitGems = 0;
        for (int index = 0; index < Math.Min(session.SummitGems.Length, 6); index++)
        {
            if (session.SummitGems[index])
                summitGems |= (byte)(1 << index);
        }

        WatchPersistentSceneState state = new(
            flags,
            summitGems,
            session.RespawnPoint,
            doNotLoadIDs.Order().ToArray(),
            session.Strawberries
                .Where(id => id.Level == room)
                .Select(id => id.ID)
                .Order()
                .ToArray(),
            level.Entities
                .OfType<Strawberry>()
                .Where(strawberry => strawberry.ID.Level == room && IsGhostSprite(strawberry.sprite))
                .Select(strawberry => strawberry.ID.ID)
                .Distinct()
                .Order()
                .ToArray()
        );
        yield return new WatchEntityState(StateKey, state.ToPayload());
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        if (states.Count != 1 || !TryParseState(states.First(), out WatchPersistentSceneState? state))
        {
            Logger.Warn(LT.MiaoNetWatch, "Ignored an invalid persistent watch scene state.");
            return WatchEntityApplyResult.None;
        }

        WatchPersistentSceneState persistentState = state!;
        Session session = level.Session;
        string room = session.Level;
        HashSet<EntityID> desiredDoNotLoad = persistentState.DoNotLoadIDs
            .Select(id => new EntityID(room, id))
            .ToHashSet();
        HashSet<EntityID> currentDoNotLoad = session.DoNotLoad
            .Where(id => id.Level == room)
            .ToHashSet();
        HashSet<EntityID> entitiesToRestore = currentDoNotLoad
            .Except(desiredDoNotLoad)
            .ToHashSet();

        HashSet<EntityID> desiredStrawberries = persistentState.StrawberryIDs
            .Select(id => new EntityID(room, id))
            .ToHashSet();
        HashSet<EntityID> currentStrawberries = session.Strawberries
            .Where(id => id.Level == room)
            .ToHashSet();
        bool strawberryStateChanged = !desiredStrawberries.SetEquals(currentStrawberries);
        HashSet<EntityID> strawberriesToRestore = currentStrawberries
            .Except(desiredStrawberries)
            .ToHashSet();

        bool desiredCassette = persistentState.Flags.HasFlag(WatchPersistentSceneFlags.Cassette);
        bool desiredHeartGem = persistentState.Flags.HasFlag(WatchPersistentSceneFlags.HeartGem);
        bool desiredFakeHeart = persistentState.Flags.HasFlag(WatchPersistentSceneFlags.FakeHeart);
        bool desiredHitCheckpoint = persistentState.Flags.HasFlag(WatchPersistentSceneFlags.HitCheckpoint);
        bool cassetteStateChanged = session.Cassette != desiredCassette;
        bool heartStateChanged = session.HeartGem != desiredHeartGem;
        bool summitStateChanged = !SummitGemsEqual(session.SummitGems, persistentState.SummitGems);
        bool restoresCassette = session.Cassette && !desiredCassette;
        bool restoresHeartGem = session.HeartGem && !desiredHeartGem;
        bool restoresSummitGem = false;
        for (int index = 0; index < Math.Min(session.SummitGems.Length, 6); index++)
            restoresSummitGem |= session.SummitGems[index]
                && (persistentState.SummitGems & (1 << index)) == 0;

        bool sessionChanged = !desiredDoNotLoad.SetEquals(currentDoNotLoad)
            || strawberryStateChanged
            || cassetteStateChanged
            || heartStateChanged
            || session.HitCheckpoint != desiredHitCheckpoint
            || session.RespawnPoint != persistentState.RespawnPoint
            || summitStateChanged;

        ApplySessionState(
            session,
            room,
            persistentState,
            desiredDoNotLoad,
            desiredStrawberries
        );

        HashSet<EntityID> restoredStrawberries = RestoreStrawberries(
            level,
            entitiesToRestore.Concat(strawberriesToRestore)
        );
        HashSet<int> locallyRestorableCrackedBlockIDs = isCompleteState
            && WatchEntitySyncRegistry.IsApplyingLifecycleReset
            ? level.Session.LevelData.Entities
                .Where(data => data.Name == "templeCrackedBlock")
                .Select(data => data.ID)
                .ToHashSet()
            : [];
        bool restoresEntities = entitiesToRestore
            .Except(restoredStrawberries)
            .Any(id => !locallyRestorableCrackedBlockIDs.Contains(id.ID));
        bool restoresStrawberries = strawberriesToRestore.Except(restoredStrawberries).Any();
        bool removedVisibleEntity = RemoveConsumedEntities(
            level,
            desiredDoNotLoad,
            desiredCassette,
            desiredHeartGem,
            desiredFakeHeart,
            persistentState.SummitGems
        );
        bool changedGhostAppearance = ApplyGhostAppearances(level, persistentState);
        bool dependentRoomState = strawberryStateChanged
            && RoomContainsAny(level, "conditionBlock", "exitBlock");
        bool heartDependentRoomState = heartStateChanged
            && RoomContainsAny(level, "whiteblock", "reflectionHeartStatue");
        bool summitDependentRoomState = summitStateChanged
            && RoomContainsAny(level, "summitGemManager");
        bool requiresReload = restoresEntities
            || restoresStrawberries
            || restoresCassette
            || restoresHeartGem
            || restoresSummitGem
            || dependentRoomState
            || heartDependentRoomState
            || summitDependentRoomState;

        bool restoredVisibleEntity = restoredStrawberries.Count > 0;
        if (!sessionChanged
            && !removedVisibleEntity
            && !restoredVisibleEntity
            && !changedGhostAppearance)
            return WatchEntityApplyResult.None;

        WatchEntityApplyResult result = WatchEntityApplyResult.SceneChanged;
        if (requiresReload)
            result |= WatchEntityApplyResult.RequiresRoomReload;
        return result;
    }

    internal static bool TryApplySessionState(Level level, WatchEntityState entityState)
    {
        if (!TryParseState(entityState, out WatchPersistentSceneState? state))
            return false;

        string room = level.Session.Level;
        WatchPersistentSceneState persistentState = state!;
        ApplySessionState(
            level.Session,
            room,
            persistentState,
            persistentState.DoNotLoadIDs.Select(id => new EntityID(room, id)).ToHashSet(),
            persistentState.StrawberryIDs.Select(id => new EntityID(room, id)).ToHashSet()
        );
        return true;
    }

    private static bool TryParseState(
        WatchEntityState entityState,
        out WatchPersistentSceneState? state
    )
    {
        state = null;
        return entityState.Key == StateKey
            && WatchPersistentSceneState.TryFromPayload(entityState.Payload.Span, out state);
    }

    private static void ApplySessionState(
        Session session,
        string room,
        WatchPersistentSceneState state,
        IReadOnlyCollection<EntityID> doNotLoad,
        IReadOnlyCollection<EntityID> strawberries
    )
    {
        ReplaceRoomIDs(session.DoNotLoad, room, doNotLoad);
        ReplaceRoomIDs(session.Strawberries, room, strawberries);
        session.Cassette = state.Flags.HasFlag(WatchPersistentSceneFlags.Cassette);
        session.HeartGem = state.Flags.HasFlag(WatchPersistentSceneFlags.HeartGem);
        session.HitCheckpoint = state.Flags.HasFlag(WatchPersistentSceneFlags.HitCheckpoint);
        session.RespawnPoint = state.RespawnPoint;
        session.SummitGems = Enumerable.Range(0, 6)
            .Select(index => (state.SummitGems & (1 << index)) != 0)
            .ToArray();
    }


    private static void ReplaceRoomIDs(
        HashSet<EntityID> destination,
        string room,
        IReadOnlyCollection<EntityID> replacement
    )
    {
        destination.RemoveWhere(id => id.Level == room);
        destination.UnionWith(replacement);
    }

    private static bool RemoveConsumedEntities(
        Level level,
        IReadOnlySet<EntityID> doNotLoad,
        bool cassette,
        bool heartGem,
        bool fakeHeart,
        byte summitGems
    )
    {
        bool removed = false;
        foreach (Entity entity in level.Entities.ToArray())
        {
            bool shouldRemove = entity is not (DashBlock or Key or LockBlock or TempleCrackedBlock)
                && TryGetPersistentEntityID(entity, out EntityID id)
                && doNotLoad.Contains(id);
            shouldRemove |= cassette && entity is Cassette;
            shouldRemove |= heartGem && entity is HeartGem { IsFake: false } or DreamHeartGem;
            shouldRemove |= fakeHeart && entity is HeartGem { IsFake: true };
            shouldRemove |= entity is SummitGem summitGem
                && (summitGems & (1 << summitGem.GemID)) != 0;
            if (!shouldRemove)
                continue;

            if (entity is HeartGem heart)
                RemoveHeartGem(level, heart);
            else
                entity.RemoveSelf();
            removed = true;
        }
        return removed;
    }

    private static HashSet<EntityID> RestoreStrawberries(
        Level level,
        IEnumerable<EntityID> requestedIDs
    )
    {
        string room = level.Session.Level;
        HashSet<EntityID> requested = requestedIDs
            .Where(id => id.Level == room)
            .ToHashSet();
        if (requested.Count == 0)
            return [];

        HashSet<int> existingIDs = level.Entities
            .OfType<Strawberry>()
            .Where(strawberry => strawberry.ID.Level == room)
            .Select(strawberry => strawberry.ID.ID)
            .ToHashSet();
        LevelData levelData = level.Session.MapData.Get(room);
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        HashSet<EntityID> restored = new();
        foreach (EntityData data in levelData.Entities)
        {
            EntityID id = new(room, data.ID);
            if (!StringComparer.Ordinal.Equals(data.Name, "strawberry")
                || !requested.Contains(id)
                || existingIDs.Contains(data.ID))
                continue;

            Strawberry strawberry = new(data, offset, id)
            {
                SourceData = data,
                SourceId = id,
            };
            level.Add(strawberry);
            existingIDs.Add(data.ID);
            restored.Add(id);
        }

        if (restored.Count > 0)
        {
            level.Entities.UpdateLists();
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Restored {restored.Count} Strawberry instance(s) in-place for room {room}."
            );
        }
        return restored;
    }

    private static void RemoveHeartGem(Level level, HeartGem heart)
    {
        heart.poem?.RemoveSelf();
        foreach (InvisibleBarrier wall in heart.walls.ToArray())
            wall.RemoveSelf();

        if (heart.IsFake)
        {
            heart.bird?.RemoveSelf();
            heart.fakeRightWall?.RemoveSelf();
            heart.FakeRemoveCameraTrigger();
            foreach (AbsorbOrb orb in level.Entities.OfType<AbsorbOrb>().ToArray())
                orb.RemoveSelf();
        }

        heart.RemoveSelf();
    }

    private static bool ApplyGhostAppearances(
        Level level,
        WatchPersistentSceneState state
    )
    {
        bool changed = false;
        HashSet<int> ghostStrawberryIDs = state.GhostStrawberryIDs.ToHashSet();
        foreach (Strawberry strawberry in level.Entities.OfType<Strawberry>())
        {
            if (strawberry.ID.Level != level.Session.Level)
                continue;

            bool shouldBeGhost = ghostStrawberryIDs.Contains(strawberry.ID.ID);
            if (IsGhostSprite(strawberry.sprite) == shouldBeGhost)
                continue;

            ReplaceStrawberrySprite(strawberry, shouldBeGhost);
            changed = true;
        }

        bool cassetteGhost = state.Flags.HasFlag(WatchPersistentSceneFlags.CassetteGhost);
        foreach (Cassette cassette in level.Entities.OfType<Cassette>())
        {
            if (cassette.IsGhost == cassetteGhost)
                continue;

            ReplaceCassetteSprite(cassette, cassetteGhost);
            changed = true;
        }

        bool heartGemGhost = state.Flags.HasFlag(WatchPersistentSceneFlags.HeartGemGhost);
        foreach (HeartGem heartGem in level.Entities.OfType<HeartGem>().Where(heart => !heart.IsFake))
        {
            if (heartGem.IsGhost == heartGemGhost)
                continue;

            ReplaceHeartGemSprite(heartGem, heartGemGhost, level.Session.Area.Mode);
            changed = true;
        }
        return changed;
    }

    private static bool IsGhostSprite(Sprite sprite)
        => SpriteIDTracker.LookupID(sprite) is "ghostberry" or "goldghostberry" or "moonghostberry";

    private static void ReplaceStrawberrySprite(Strawberry strawberry, bool ghost)
    {
        Sprite previous = strawberry.sprite;
        string spriteID = strawberry.Moon
            ? ghost ? "moonghostberry" : "moonberry"
            : strawberry.Golden
                ? ghost ? "goldghostberry" : "goldberry"
                : ghost ? "ghostberry" : "strawberry";
        Sprite replacement = GFX.SpriteBank.Create(spriteID);
        CopySpritePresentation(previous, replacement);
        replacement.Color = ghost ? Color.White * 0.8f : Color.White;
        replacement.OnFrameChange = previous.OnFrameChange;
        strawberry.Remove(previous);
        strawberry.sprite = replacement;
        strawberry.Add(replacement);
        if (strawberry.Winged)
            replacement.Play("flap");
    }

    private static void ReplaceCassetteSprite(Cassette cassette, bool ghost)
    {
        Sprite previous = cassette.sprite;
        Sprite replacement = GFX.SpriteBank.Create(ghost ? "cassetteGhost" : "cassette");
        CopySpritePresentation(previous, replacement);
        replacement.Color = ghost ? Color.White * 0.8f : Color.White;
        cassette.Remove(previous);
        cassette.IsGhost = ghost;
        cassette.sprite = replacement;
        cassette.Add(replacement);
        replacement.Play("idle");
    }

    private static void ReplaceHeartGemSprite(HeartGem heartGem, bool ghost, AreaMode mode)
    {
        Sprite previous = heartGem.sprite;
        Sprite replacement = GFX.SpriteBank.Create(ghost ? "heartGemGhost" : $"heartgem{(int)mode}");
        CopySpritePresentation(previous, replacement);
        replacement.Color = ghost ? Color.White * 0.8f : Color.White;
        replacement.OnLoop = previous.OnLoop;
        heartGem.Remove(previous);
        heartGem.IsGhost = ghost;
        heartGem.sprite = replacement;
        heartGem.Add(replacement);
        replacement.Play("spin");
    }

    private static void CopySpritePresentation(Sprite source, Sprite destination)
    {
        destination.Position = source.Position;
        destination.Scale = source.Scale;
        destination.Rotation = source.Rotation;
        destination.Visible = source.Visible;
    }

    private static bool TryGetPersistentEntityID(Entity entity, out EntityID id)
    {
        switch (entity)
        {
            case Strawberry strawberry:
                id = strawberry.ID;
                return true;
            case SummitGem summitGem:
                id = summitGem.GID;
                return true;
            case Key key:
                id = key.ID;
                return true;
            case LockBlock lockBlock:
                id = lockBlock.ID;
                return true;
            case DashBlock dashBlock:
                id = dashBlock.id;
                return true;
            case FakeWall fakeWall:
                id = fakeWall.eid;
                return true;
            case TempleCrackedBlock crackedBlock:
                id = crackedBlock.eid;
                return true;
            case CrumbleWallOnRumble crumbleWall:
                id = crumbleWall.id;
                return true;
            default:
                id = default;
                return false;
        }
    }

    private static bool SummitGemsEqual(bool[] current, byte expected)
    {
        for (int index = 0; index < 6; index++)
        {
            bool currentValue = index < current.Length && current[index];
            if (currentValue != ((expected & (1 << index)) != 0))
                return false;
        }
        return true;
    }

    private static bool RoomContainsAny(Level level, params string[] entityNames)
    {
        HashSet<string> names = new(entityNames, StringComparer.Ordinal);
        return level.Session.MapData.Get(level.Session.Level).Entities.Any(data => names.Contains(data.Name));
    }
}
