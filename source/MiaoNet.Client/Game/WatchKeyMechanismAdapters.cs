using MiaoNet.Shared;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchKeyAdapter : IWatchEntityAdapter
{
    private const byte CollectEvent = 1;
    private const byte UseEvent = 2;
    private const byte GoneEvent = 3;
    private const int PayloadSize = 12;

    private sealed class KeyInfo
    {
        public string Level { get; }
        public int ID { get; }
        public WatchEntityPhase Phase { get; set; }

        public KeyInfo(EntityID id)
        {
            Level = id.Level;
            ID = id.ID;
            Phase = WatchEntityPhase.Ready;
        }
    }

    private sealed class RemoteUseState
    {
        public int Generation { get; set; }
        public bool Active { get; set; }
    }

    private static readonly WatchKeyAdapter instance = new();
    private static readonly ConditionalWeakTable<Key, KeyInfo> infos = new();
    private static readonly Dictionary<(string Level, int ID), WatchEntityPhase> phases = new();
    private static readonly ConditionalWeakTable<Key, RemoteUseState> remoteUses = new();

    public WatchEntityKind Kind => WatchEntityKind.Key;

    public static void Load()
    {
        On.Celeste.Key.ctor_EntityData_Vector2_EntityID += Key_ctor_EntityData;
        On.Celeste.Key.ctor_Player_EntityID += Key_ctor_Player;
        On.Celeste.Key.OnPlayer += Key_OnPlayer;
        On.Celeste.Key.UseRoutine += Key_UseRoutine;
        On.Celeste.Key.RegisterUsed += Key_RegisterUsed;
        On.Celeste.Key.Update += Key_Update;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.Key.Update -= Key_Update;
        On.Celeste.Key.RegisterUsed -= Key_RegisterUsed;
        On.Celeste.Key.UseRoutine -= Key_UseRoutine;
        On.Celeste.Key.OnPlayer -= Key_OnPlayer;
        On.Celeste.Key.ctor_Player_EntityID -= Key_ctor_Player;
        On.Celeste.Key.ctor_EntityData_Vector2_EntityID -= Key_ctor_EntityData;
        remoteUses.Clear();
        phases.Clear();
        infos.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> live = new();
        foreach (Key key in level.Entities.OfType<Key>())
        {
            if (!infos.TryGetValue(key, out KeyInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, room))
                continue;

            WatchEntityPhase phase = DeterminePhase(key, info);
            phases[(room, info.ID)] = phase;
            live.Add(info.ID);
            yield return Encode(info.ID, phase, key);
        }

        foreach (((string levelName, int id), WatchEntityPhase phase) in phases
            .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Level, room)
                && !live.Contains(pair.Key.ID))
            .OrderBy(pair => pair.Key.ID))
        {
            _ = levelName;
            yield return Encode(id, phase, null);
        }

        foreach (int id in level.Session.LevelData.Entities
            .Where(data => data.Name == "key"
                && level.Session.DoNotLoad.Contains(new EntityID(room, data.ID)))
            .Select(data => data.ID)
            .Where(id => !live.Contains(id) && !phases.ContainsKey((room, id)))
            .Order())
        {
            phases[(room, id)] = WatchEntityPhase.Gone;
            yield return Encode(id, WatchEntityPhase.Gone, null);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        string room = level.Session.Level;
        if (isCompleteState)
            changed |= RestoreMissingReadyKeys(level, desired);
        foreach (Key key in level.Entities.OfType<Key>().ToArray())
        {
            if (!infos.TryGetValue(key, out KeyInfo? info)
                || !StringComparer.Ordinal.Equals(info.Level, room)
                || !desired.Remove(info.ID, out WatchEntityState state))
                continue;

            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase phase = (WatchEntityPhase)payload[0];
            Vector2 position = WatchEntityPayloadCodec.ReadVector2(payload, 4);
            bool visible = (payload[1] & 1) != 0;
            bool collidable = (payload[1] & 2) != 0;
            bool turning = (payload[1] & 4) != 0;

            info.Phase = phase;
            phases[(room, info.ID)] = phase;
            key.Position = position;
            key.Turning = turning;
            key.Collidable = phase == WatchEntityPhase.Ready && collidable;
            key.Visible = phase is WatchEntityPhase.Ready or WatchEntityPhase.Returning && visible;
            key.sprite.Visible = key.Visible;
            if (phase == WatchEntityPhase.Gone)
            {
                CancelRemoteUse(key);
                key.RemoveSelf();
            }
            changed = true;
        }

        // A missing Ready key means the local room was loaded with a conflicting DoNotLoad state.
        bool reload = desired.Values.Any(state => state.Payload.Span[0] == (byte)WatchEntityPhase.Ready);
        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (reload)
            result |= WatchEntityApplyResult.RequiresRoomReload | WatchEntityApplyResult.SceneChanged;
        return result;
    }

    private static bool RestoreMissingReadyKeys(
        Level level,
        IReadOnlyDictionary<int, WatchEntityState> desired
    )
    {
        string room = level.Session.Level;
        HashSet<int> existing = level.Entities.OfType<Key>()
            .Select(key => infos.TryGetValue(key, out KeyInfo? info)
                && StringComparer.Ordinal.Equals(info.Level, room)
                    ? info.ID
                    : -1)
            .Where(id => id >= 0)
            .ToHashSet();
        HashSet<int> missing = desired
            .Where(pair => pair.Value.Payload.Span[0] == (byte)WatchEntityPhase.Ready
                && !existing.Contains(pair.Key))
            .Select(pair => pair.Key)
            .ToHashSet();
        if (missing.Count == 0)
            return false;

        LevelData levelData = level.Session.MapData.Get(room);
        Vector2 offset = new(levelData.Bounds.Left, levelData.Bounds.Top);
        int restored = 0;
        foreach (EntityData data in levelData.Entities)
        {
            if (data.Name != "key" || !missing.Remove(data.ID))
                continue;

            EntityID id = new(room, data.ID);
            Key key = new(data, offset, id)
            {
                SourceData = data,
                SourceId = id,
            };
            level.Add(key);
            restored++;
        }

        if (restored > 0)
        {
            level.Entities.UpdateLists();
            Logger.Debug(
                LT.MiaoNetWatch,
                $"Restored {restored} Key instance(s) in-place for room {room}."
            );
        }
        return restored > 0;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        Key? key = Find(level, entityEvent.Key.EntityID);
        if (key is null)
            return;

        switch (entityEvent.EventID)
        {
            case CollectEvent when entityEvent.Payload.Length == 0:
                key.Collidable = false;
                key.Visible = false;
                key.sprite.Visible = false;
                level.Particles.Emit(Key.P_Collect, 10, key.Position, Vector2.One * 3f);
                Audio.Play("event:/game/general/key_get", key.Position);
                break;

            case UseEvent when entityEvent.Payload.Length == PayloadSize:
                if (IsRemoteUsing(key))
                    return;
                ReadOnlySpan<byte> payload = entityEvent.Payload.Span;
                Vector2 target = WatchEntityPayloadCodec.ReadVector2(payload, 4);
                key.Visible = true;
                key.sprite.Visible = true;
                key.Collidable = false;
                StartRemoteUse(key, target, removeWhenFinished: true);
                break;

            case GoneEvent when entityEvent.Payload.Length == 0:
                CancelRemoteUse(key);
                key.Visible = false;
                key.Collidable = false;
                key.RemoveSelf();
                break;
        }
    }

    internal static Key? Find(Level level, int id)
        => level.Entities.OfType<Key>().FirstOrDefault(key =>
            infos.TryGetValue(key, out KeyInfo? info)
            && StringComparer.Ordinal.Equals(info.Level, level.Session.Level)
            && info.ID == id
        );

    internal static int BeginRemoteUse(Key key)
    {
        RemoteUseState state = remoteUses.GetValue(key, static _ => new());
        state.Generation++;
        state.Active = true;
        key.StartedUsing = true;
        key.Collidable = false;
        if (infos.TryGetValue(key, out KeyInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchEntityPhase.Returning;
        return state.Generation;
    }

    internal static bool IsRemoteUseCurrent(Key key, int generation)
        => remoteUses.TryGetValue(key, out RemoteUseState? state)
            && state.Active
            && state.Generation == generation;

    internal static void CancelRemoteUse(Key key, bool remove = false)
    {
        if (remoteUses.TryGetValue(key, out RemoteUseState? state))
        {
            state.Generation++;
            state.Active = false;
        }
        key.StartedUsing = false;
        key.Turning = false;
        if (remove)
            key.RemoveSelf();
    }

    internal static IEnumerator PlayRemoteUse(Key key, Vector2 target, int generation)
    {
        float waitForScene = 0f;
        while (key.Scene is null && waitForScene < 0.25f)
        {
            if (!IsRemoteUseCurrent(key, generation))
                yield break;
            waitForScene += Engine.DeltaTime;
            yield return null;
        }
        if (!IsRemoteUseCurrent(key, generation) || key.Scene is not Level)
            yield break;

        key.Turning = true;
        key.Visible = true;
        key.sprite.Visible = true;
        key.sprite.Y = 0f;
        key.follower.MoveTowardsLeader = false;
        key.wiggler.Start();
        key.wobbleActive = false;

        Vector2 start = key.Position;
        Vector2 control = (start + target) / 2f + new Vector2(0f, -48f);
        float elapsed = 0f;
        while (elapsed < 1f)
        {
            if (!IsRemoteUseCurrent(key, generation) || key.Scene is not Level)
                yield break;
            elapsed = Math.Min(1f, elapsed + Engine.DeltaTime);
            float progress = 1f - MathF.Pow(1f - elapsed, 3f);
            Vector2 first = Vector2.Lerp(start, control, progress);
            Vector2 second = Vector2.Lerp(control, target, progress);
            key.Position = Vector2.Lerp(first, second, progress);
            key.sprite.Rate = 1f + progress * 2f;
            yield return null;
        }

        if (!IsRemoteUseCurrent(key, generation) || key.Scene is not Level level)
            yield break;
        key.Position = target;
        key.shimmerParticles?.RemoveSelf();
        for (int i = 0; i < 16; i++)
            level.ParticlesFG.Emit(Key.P_Insert, key.Center, MathF.PI / 8f * i);
        key.sprite.Play("enter");

        elapsed = 0f;
        float startRotation = key.sprite.Rotation;
        while (elapsed < 0.3f)
        {
            if (!IsRemoteUseCurrent(key, generation) || key.Scene is not Level)
                yield break;
            elapsed = Math.Min(0.3f, elapsed + Engine.DeltaTime);
            float progress = elapsed / 0.3f;
            key.sprite.Rotation = startRotation + progress * MathF.PI / 2f;
            yield return null;
        }

        if (!IsRemoteUseCurrent(key, generation) || key.Scene is not Level finalLevel)
            yield break;
        for (int i = 0; i < 8; i++)
            finalLevel.ParticlesFG.Emit(Key.P_Insert, key.Center, MathF.PI / 4f * i);
        key.sprite.Visible = false;
        key.Turning = false;
    }

    internal static void CompleteRemoteUse(Key key, int generation, bool remove)
    {
        if (!IsRemoteUseCurrent(key, generation))
            return;
        RemoteUseState state = remoteUses.GetValue(key, static _ => new());
        state.Active = false;
        key.StartedUsing = false;
        key.Turning = false;
        if (infos.TryGetValue(key, out KeyInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchEntityPhase.Gone;
        if (remove)
            key.RemoveSelf();
    }

    private static bool IsRemoteUsing(Key key)
        => remoteUses.TryGetValue(key, out RemoteUseState? state) && state.Active;

    private static void StartRemoteUse(Key key, Vector2 target, bool removeWhenFinished)
    {
        int generation = BeginRemoteUse(key);
        key.Add(new Coroutine(RunRemoteUse(key, target, generation, removeWhenFinished)));
    }

    private static IEnumerator RunRemoteUse(
        Key key,
        Vector2 target,
        int generation,
        bool removeWhenFinished
    )
    {
        yield return PlayRemoteUse(key, target, generation);
        CompleteRemoteUse(key, generation, removeWhenFinished);
    }

    internal static Key CreateRemoteKey(Level level, int id, Vector2 position)
    {
        EntityID entityID = new(level.Session.Level, id);
        Key key = new(position, entityID, Array.Empty<Vector2>());
        Track(key, entityID);
        level.Add(key);
        return key;
    }

    private static WatchEntityPhase DeterminePhase(Key key, KeyInfo info)
    {
        if (key.IsUsed && !key.sprite.Visible)
            info.Phase = WatchEntityPhase.Gone;
        else if (key.StartedUsing || key.Turning)
            info.Phase = WatchEntityPhase.Returning;
        else if (key.follower.Leader is not null || !key.Collidable)
            info.Phase = WatchEntityPhase.Active;
        else
            info.Phase = WatchEntityPhase.Ready;
        return info.Phase;
    }

    private static WatchEntityState Encode(int id, WatchEntityPhase phase, Key? key)
    {
        byte[] payload = new byte[PayloadSize];
        payload[0] = (byte)phase;
        if (key is not null)
        {
            if (key.Visible && key.sprite.Visible)
                payload[1] |= 1;
            if (key.Collidable)
                payload[1] |= 2;
            if (key.Turning)
                payload[1] |= 4;
            WatchEntityPayloadCodec.WriteVector2(payload, 4, key.Position);
        }
        return new(new WatchEntityKey(WatchEntityKind.Key, id), payload);
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.Key
            && state.Key.SubID == 0
            && payload.Length == PayloadSize
            && payload[0] <= (byte)WatchEntityPhase.Returning
            && (payload[1] & ~0b0000_0111) == 0
            && payload[2] == 0 && payload[3] == 0
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 4))
            && float.IsFinite(WatchEntityPayloadCodec.ReadSingle(payload, 8));
    }

    private static void Track(Key key, EntityID id)
    {
        KeyInfo info = new(id);
        infos.AddOrUpdate(key, info);
        phases[(id.Level, id.ID)] = WatchEntityPhase.Ready;
    }

    private static void Publish(Key self, byte eventID, ReadOnlySpan<byte> payload)
    {
        if (self.Scene is not Level level
            || !infos.TryGetValue(self, out KeyInfo? info)
            || !StringComparer.Ordinal.Equals(info.Level, level.Session.Level))
            return;
        WatchEntitySyncRegistry.PublishEvent(
            level,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.Key, info.ID), eventID, payload)
        );
    }

    private static void Key_ctor_EntityData(
        On.Celeste.Key.orig_ctor_EntityData_Vector2_EntityID orig,
        Key self,
        EntityData data,
        Vector2 offset,
        EntityID id
    )
    {
        orig(self, data, offset, id);
        Track(self, id);
    }

    private static void Key_ctor_Player(
        On.Celeste.Key.orig_ctor_Player_EntityID orig,
        Key self,
        Player player,
        EntityID id
    )
    {
        orig(self, player, id);
        Track(self, id);
        if (infos.TryGetValue(self, out KeyInfo? info))
            info.Phase = phases[(id.Level, id.ID)] = WatchEntityPhase.Active;
    }

    private static void Key_OnPlayer(
        On.Celeste.Key.orig_OnPlayer orig,
        Key self,
        Player player
    )
    {
        bool wasCollidable = self.Collidable;
        orig(self, player);
        if (!wasCollidable || self.Collidable
            || WatchEntitySyncRegistry.IsApplyingRemoteState
            || IsRemoteUsing(self))
            return;

        if (infos.TryGetValue(self, out KeyInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchEntityPhase.Active;
        Publish(self, CollectEvent, []);
    }

    private static IEnumerator Key_UseRoutine(
        On.Celeste.Key.orig_UseRoutine orig,
        Key self,
        Vector2 target
    )
    {
        if (infos.TryGetValue(self, out KeyInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchEntityPhase.Returning;

        if (!WatchEntitySyncRegistry.IsApplyingRemoteState && !IsRemoteUsing(self))
        {
            byte[] payload = new byte[PayloadSize];
            WatchEntityPayloadCodec.WriteInt32(payload, 0, 0);
            WatchEntityPayloadCodec.WriteVector2(payload, 4, target);
            Publish(self, UseEvent, payload);
        }
        return orig(self, target);
    }

    private static void Key_RegisterUsed(
        On.Celeste.Key.orig_RegisterUsed orig,
        Key self
    )
    {
        if (IsRemoteUsing(self))
            return;
        orig(self);
        if (infos.TryGetValue(self, out KeyInfo? info))
            info.Phase = phases[(info.Level, info.ID)] = WatchEntityPhase.Returning;
    }

    private static void Key_Update(On.Celeste.Key.orig_Update orig, Key self)
    {
        bool wasGone = infos.TryGetValue(self, out KeyInfo? info)
            && info.Phase == WatchEntityPhase.Gone;
        orig(self);
        if (info is null || !self.IsUsed || self.sprite.Visible)
            return;

        info.Phase = phases[(info.Level, info.ID)] = WatchEntityPhase.Gone;
        if (!wasGone && !IsRemoteUsing(self) && !WatchEntitySyncRegistry.IsApplyingRemoteState)
            Publish(self, GoneEvent, []);
    }
}

internal sealed class WatchLockBlockAdapter : IWatchEntityAdapter
{
    private const byte UnlockEvent = 1;

    private sealed record RemoteUnlockInfo(Key Key, int Generation);

    private static readonly WatchLockBlockAdapter instance = new();
    private static readonly Dictionary<(string Level, int ID), WatchEntityPhase> phases = new();
    private static readonly HashSet<LockBlock> remoteUnlocks = new();
    private static readonly ConditionalWeakTable<LockBlock, RemoteUnlockInfo> remoteUnlockInfo = new();

    public WatchEntityKind Kind => WatchEntityKind.LockBlock;

    public static void Load()
    {
        On.Celeste.LockBlock.ctor_EntityData_Vector2_EntityID += LockBlock_ctor;
        On.Celeste.LockBlock.TryOpen += LockBlock_TryOpen;
        On.Celeste.LockBlock.UnlockRoutine += LockBlock_UnlockRoutine;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.LockBlock.UnlockRoutine -= LockBlock_UnlockRoutine;
        On.Celeste.LockBlock.TryOpen -= LockBlock_TryOpen;
        On.Celeste.LockBlock.ctor_EntityData_Vector2_EntityID -= LockBlock_ctor;
        remoteUnlocks.Clear();
        remoteUnlockInfo.Clear();
        phases.Clear();
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        string room = level.Session.Level;
        HashSet<int> live = new();
        foreach (LockBlock block in level.Entities.OfType<LockBlock>())
        {
            if (block.ID.Level != room)
                continue;
            WatchEntityPhase phase = block.opening
                ? WatchEntityPhase.Active
                : WatchEntityPhase.Ready;
            phases[(room, block.ID.ID)] = phase;
            live.Add(block.ID.ID);
            yield return Encode(block.ID.ID, phase, block);
        }
        foreach (((string levelName, int id), WatchEntityPhase phase) in phases
            .Where(pair => pair.Key.Level == room && !live.Contains(pair.Key.ID))
            .OrderBy(pair => pair.Key.ID))
        {
            _ = levelName;
            yield return Encode(id, phase, null);
        }
        foreach (int id in level.Session.LevelData.Entities
            .Where(data => data.Name == "lockBlock"
                && level.Session.DoNotLoad.Contains(new EntityID(room, data.ID)))
            .Select(data => data.ID)
            .Where(id => !live.Contains(id) && !phases.ContainsKey((room, id)))
            .Order())
        {
            phases[(room, id)] = WatchEntityPhase.Gone;
            yield return Encode(id, WatchEntityPhase.Gone, null);
        }
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        Dictionary<int, WatchEntityState> desired = new();
        foreach (WatchEntityState state in states)
        {
            if (!TryValidate(state) || !desired.TryAdd(state.Key.EntityID, state))
                return WatchEntityApplyResult.None;
        }

        bool changed = false;
        foreach (LockBlock block in level.Entities.OfType<LockBlock>().ToArray())
        {
            if (block.ID.Level != level.Session.Level
                || !desired.Remove(block.ID.ID, out WatchEntityState state))
                continue;
            ReadOnlySpan<byte> payload = state.Payload.Span;
            WatchEntityPhase phase = (WatchEntityPhase)payload[0];
            phases[(block.ID.Level, block.ID.ID)] = phase;
            block.opening = phase == WatchEntityPhase.Active;
            block.Visible = phase != WatchEntityPhase.Gone && (payload[1] & 1) != 0;
            block.Collidable = phase == WatchEntityPhase.Ready && (payload[1] & 2) != 0;
            if (phase == WatchEntityPhase.Gone)
            {
                CancelRemoteUnlock(block);
                block.RemoveSelf();
            }
            changed = true;
        }

        bool reload = desired.Values.Any(state => state.Payload.Span[0] == (byte)WatchEntityPhase.Ready);
        WatchEntityApplyResult result = changed
            ? WatchEntityApplyResult.SceneChanged
            : WatchEntityApplyResult.None;
        if (reload)
            result |= WatchEntityApplyResult.RequiresRoomReload | WatchEntityApplyResult.SceneChanged;
        return result;
    }

    public void ApplyEvent(Level level, WatchEntityEvent entityEvent)
    {
        if (entityEvent.EventID != UnlockEvent || entityEvent.Payload.Length != 4)
            return;
        LockBlock? block = level.Entities.OfType<LockBlock>().FirstOrDefault(candidate =>
            candidate.ID.Level == level.Session.Level
            && candidate.ID.ID == entityEvent.Key.EntityID
        );
        if (block is null || remoteUnlocks.Contains(block))
            return;

        int keyID = WatchEntityPayloadCodec.ReadInt32(entityEvent.Payload.Span, 0);
        Key? key = WatchKeyAdapter.Find(level, keyID);
        block.opening = true;
        block.Collidable = false;
        remoteUnlocks.Add(block);
        if (key is null && keyID >= 0)
            key = WatchKeyAdapter.CreateRemoteKey(level, keyID, block.Center - Vector2.UnitX * 24f);
        if (key is not null)
        {
            int generation = WatchKeyAdapter.BeginRemoteUse(key);
            remoteUnlockInfo.AddOrUpdate(block, new RemoteUnlockInfo(key, generation));
            block.Add(new Coroutine(RemoteUnlockRoutine(block, key, generation)));
        }
        else
        {
            block.Add(new Coroutine(FallbackUnlockRoutine(block)));
        }
    }

    private static IEnumerator RemoteUnlockRoutine(LockBlock block, Key key, int generation)
    {
        if (MiaoNetModule.Settings.PlayerAudioSyncMode.HasReceive)
            Audio.Play(block.unlockSfxName, block.Center);

        yield return WatchKeyAdapter.PlayRemoteUse(
            key,
            block.Center + new Vector2(0f, 2f),
            generation
        );
        if (!WatchKeyAdapter.IsRemoteUseCurrent(key, generation)
            || block.Scene is not Level level)
        {
            remoteUnlocks.Remove(block);
            remoteUnlockInfo.Remove(block);
            yield break;
        }

        block.UnlockingRegistered = true;
        block.Tag |= Tags.TransitionUpdate;
        block.Collidable = false;
        yield return block.sprite.PlayRoutine("open", restart: false);
        if (!WatchKeyAdapter.IsRemoteUseCurrent(key, generation)
            || block.Scene is not Level)
        {
            remoteUnlocks.Remove(block);
            remoteUnlockInfo.Remove(block);
            yield break;
        }

        level.Shake();
        yield return block.sprite.PlayRoutine("burst", restart: false);
        WatchKeyAdapter.CompleteRemoteUse(key, generation, remove: true);
        remoteUnlocks.Remove(block);
        remoteUnlockInfo.Remove(block);
        phases[(block.ID.Level, block.ID.ID)] = WatchEntityPhase.Gone;
        block.RemoveSelf();
    }

    private static IEnumerator FallbackUnlockRoutine(LockBlock block)
    {
        Audio.Play(block.unlockSfxName, block.Center);
        yield return block.sprite.PlayRoutine("open", restart: false);
        if (block.Scene is Level level)
            level.Shake();
        yield return block.sprite.PlayRoutine("burst", restart: false);
        phases[(block.ID.Level, block.ID.ID)] = WatchEntityPhase.Gone;
        remoteUnlocks.Remove(block);
        block.RemoveSelf();
    }

    private static void CancelRemoteUnlock(LockBlock block)
    {
        if (remoteUnlockInfo.TryGetValue(block, out RemoteUnlockInfo? info))
        {
            WatchKeyAdapter.CancelRemoteUse(info.Key, remove: true);
            remoteUnlockInfo.Remove(block);
        }
        remoteUnlocks.Remove(block);
    }

    private static WatchEntityState Encode(int id, WatchEntityPhase phase, LockBlock? block)
    {
        byte[] payload = new byte[4];
        payload[0] = (byte)phase;
        if (block is not null)
        {
            if (block.Visible)
                payload[1] |= 1;
            if (block.Collidable)
                payload[1] |= 2;
            if (block.UnlockingRegistered)
                payload[1] |= 4;
        }
        return new(new WatchEntityKey(WatchEntityKind.LockBlock, id), payload);
    }

    private static bool TryValidate(WatchEntityState state)
    {
        ReadOnlySpan<byte> payload = state.Payload.Span;
        return state.Key.Kind == WatchEntityKind.LockBlock
            && state.Key.SubID == 0
            && payload.Length == 4
            && payload[0] <= (byte)WatchEntityPhase.Gone
            && (payload[1] & ~0b0000_0111) == 0
            && payload[2] == 0 && payload[3] == 0;
    }

    private static void LockBlock_ctor(
        On.Celeste.LockBlock.orig_ctor_EntityData_Vector2_EntityID orig,
        LockBlock self,
        EntityData data,
        Vector2 offset,
        EntityID id
    )
    {
        orig(self, data, offset, id);
        phases[(id.Level, id.ID)] = WatchEntityPhase.Ready;
    }

    private static void LockBlock_TryOpen(
        On.Celeste.LockBlock.orig_TryOpen orig,
        LockBlock self,
        Player player,
        Follower follower
    )
    {
        bool wasOpening = self.opening;
        orig(self, player, follower);
        if (wasOpening || !self.opening
            || remoteUnlocks.Contains(self)
            || WatchEntitySyncRegistry.IsApplyingRemoteState)
            return;

        phases[(self.ID.Level, self.ID.ID)] = WatchEntityPhase.Active;
        int keyID = follower.Entity is Key key ? key.ID.ID : -1;
        byte[] payload = new byte[4];
        WatchEntityPayloadCodec.WriteInt32(payload, 0, keyID);
        if (self.Scene is Level level)
        {
            WatchEntitySyncRegistry.PublishEvent(
                level,
                new WatchEntityEvent(
                    new WatchEntityKey(WatchEntityKind.LockBlock, self.ID.ID),
                    UnlockEvent,
                    payload
                )
            );
        }
    }

    private static IEnumerator LockBlock_UnlockRoutine(
        On.Celeste.LockBlock.orig_UnlockRoutine orig,
        LockBlock self,
        Follower follower
    )
    {
        IEnumerator inner = orig(self, follower);
        if (remoteUnlocks.Contains(self) || WatchEntitySyncRegistry.IsApplyingRemoteState)
            return inner;
        phases[(self.ID.Level, self.ID.ID)] = WatchEntityPhase.Active;
        return TrackUnlock(self, inner);
    }

    private static IEnumerator TrackUnlock(LockBlock block, IEnumerator inner)
    {
        yield return inner;
        phases[(block.ID.Level, block.ID.ID)] = WatchEntityPhase.Gone;
    }
}
