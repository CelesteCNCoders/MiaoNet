using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchStaticSpinnerAdapter : IWatchEntityAdapter
{
    private const byte Destroyed = 1;
    private const byte DestroyEvent = 1;

    private static readonly WatchStaticSpinnerAdapter instance = new();
    private static readonly Dictionary<string, HashSet<int>> destroyedByRoom = new(StringComparer.Ordinal);
    private static readonly HashSet<int> remoteDestroyed = new();
    private static string? remoteRoom;

    public WatchEntityKind Kind => WatchEntityKind.StaticSpinner;

    public static void Load()
    {
        On.Celeste.CrystalStaticSpinner.ctor_EntityData_Vector2_CrystalColor += Crystal_ctor;
        On.Celeste.CrystalStaticSpinner.Destroy += Crystal_Destroy;
        On.Celeste.CrystalStaticSpinner.OnPlayer += Crystal_OnPlayer;
        On.Celeste.CrystalStaticSpinner.OnHoldable += Crystal_OnHoldable;
        On.Celeste.DustStaticSpinner.OnPlayer += Dust_OnPlayer;
        On.Celeste.DustStaticSpinner.OnHoldable += Dust_OnHoldable;
        WatchEntitySyncRegistry.Register(instance);
    }

    public static void Unload()
    {
        WatchEntitySyncRegistry.Unregister(instance);
        On.Celeste.DustStaticSpinner.OnHoldable -= Dust_OnHoldable;
        On.Celeste.DustStaticSpinner.OnPlayer -= Dust_OnPlayer;
        On.Celeste.CrystalStaticSpinner.OnHoldable -= Crystal_OnHoldable;
        On.Celeste.CrystalStaticSpinner.OnPlayer -= Crystal_OnPlayer;
        On.Celeste.CrystalStaticSpinner.Destroy -= Crystal_Destroy;
        On.Celeste.CrystalStaticSpinner.ctor_EntityData_Vector2_CrystalColor -= Crystal_ctor;
        WatchEntityIDTable<CrystalStaticSpinner>.Clear();
        destroyedByRoom.Clear();
        remoteDestroyed.Clear();
        remoteRoom = null;
    }

    public IEnumerable<WatchEntityState> CaptureStates(Level level)
    {
        if (!destroyedByRoom.TryGetValue(level.Session.Level, out HashSet<int>? ids))
            yield break;

        foreach (int id in ids.Order())
            yield return new(new WatchEntityKey(Kind, id), [Destroyed]);
    }

    public WatchEntityApplyResult ApplyStates(
        Level level,
        IReadOnlyCollection<WatchEntityState> states,
        bool isCompleteState
    )
    {
        string room = level.Session.Level;
        HashSet<int> previousDestroyed = StringComparer.Ordinal.Equals(remoteRoom, room)
            ? new(remoteDestroyed)
            : [];
        if (isCompleteState || !StringComparer.Ordinal.Equals(remoteRoom, room))
        {
            remoteDestroyed.Clear();
            remoteRoom = room;
        }

        HashSet<int> packetDestroyed = new();
        foreach (WatchEntityState state in states)
        {
            if (state.Key.Kind != Kind
                || state.Key.SubID != 0
                || state.Payload.Length != 1
                || state.Payload.Span[0] != Destroyed
                || !packetDestroyed.Add(state.Key.EntityID))
                return WatchEntityApplyResult.None;
            remoteDestroyed.Add(state.Key.EntityID);
        }

        bool changed = false;
        bool requiresReload = false;
        HashSet<int> found = new();
        foreach (CrystalStaticSpinner spinner in level.Entities.OfType<CrystalStaticSpinner>())
        {
            if (!WatchEntityIDTable<CrystalStaticSpinner>.TryGet(spinner, room, out int id))
                continue;
            found.Add(id);

            bool wasDestroyed = previousDestroyed.Contains(id);
            bool isDestroyed = remoteDestroyed.Contains(id);
            if (isDestroyed)
            {
                changed |= !wasDestroyed || spinner.Visible || spinner.Collidable;
                spinner.Visible = false;
                spinner.Collidable = false;
            }
            else if (wasDestroyed)
            {
                // CrystalStaticSpinner starts invisible and creates its sprites the
                // first time its vanilla Update sees it enter the camera. Restoring
                // Visible directly would skip that path and leave expanded=false
                // with no Image components, so return it to the vanilla activation
                // state instead.
                spinner.Visible = false;
                spinner.Collidable = false;
                changed = true;
            }
        }
        if (isCompleteState
            && previousDestroyed.Any(id => !remoteDestroyed.Contains(id) && !found.Contains(id)))
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
        if (entityEvent.Key.Kind != Kind
            || entityEvent.Key.SubID != 0
            || entityEvent.EventID != DestroyEvent
            || entityEvent.Payload.Length != 1)
            return;

        string room = level.Session.Level;
        CrystalStaticSpinner? spinner = level.Entities.OfType<CrystalStaticSpinner>()
            .FirstOrDefault(candidate =>
                WatchEntityIDTable<CrystalStaticSpinner>.TryGet(candidate, room, out int id)
                && id == entityEvent.Key.EntityID
            );
        if (spinner is null)
            return;

        PlayRemoteDestroy(spinner, entityEvent.Payload.Span[0] != 0);
    }

    private static void PlayRemoteDestroy(CrystalStaticSpinner spinner, bool boss)
    {
        // Keep the map entity alive on a Watcher. A later death/respawn snapshot
        // can then restore it without LoadLevel, while the one-shot still looks
        // identical to CrystalStaticSpinner.Destroy.
        if (spinner.InView())
        {
            Audio.Play("event:/game/06_reflection/fall_spike_smash", spinner.Position);
            Color color = spinner.color switch
            {
                CrystalColor.Red => Calc.HexToColor("ff4f4f"),
                CrystalColor.Blue => Calc.HexToColor("639bff"),
                CrystalColor.Purple => Calc.HexToColor("ff4fef"),
                _ => Color.White,
            };
            CrystalDebris.Burst(spinner.Position, color, boss, 8);
        }
        spinner.Visible = false;
        spinner.Collidable = false;
    }

    private static void Crystal_ctor(
        On.Celeste.CrystalStaticSpinner.orig_ctor_EntityData_Vector2_CrystalColor orig,
        CrystalStaticSpinner self,
        EntityData data,
        Vector2 offset,
        CrystalColor color
    )
    {
        orig(self, data, offset, color);
        string room = data.Level.Name;
        WatchEntityIDTable<CrystalStaticSpinner>.Set(self, room, data.ID);
        if (destroyedByRoom.TryGetValue(room, out HashSet<int>? ids))
            ids.Remove(data.ID);
    }

    private static void Crystal_Destroy(
        On.Celeste.CrystalStaticSpinner.orig_Destroy orig,
        CrystalStaticSpinner self,
        bool boss
    )
    {
        Level? level = self.Scene as Level;
        string? room = level?.Session.Level;
        int id = -1;
        bool publish = level is not null
            && room is not null
            && !WatchEntitySyncRegistry.IsApplyingRemoteState
            && WatchEntityIDTable<CrystalStaticSpinner>.TryGet(self, room, out id);

        orig(self, boss);
        if (!publish)
            return;

        if (!destroyedByRoom.TryGetValue(room!, out HashSet<int>? ids))
            destroyedByRoom[room!] = ids = new();
        ids.Add(id);
        WatchEntitySyncRegistry.PublishEvent(
            level!,
            new WatchEntityEvent(new WatchEntityKey(WatchEntityKind.StaticSpinner, id), DestroyEvent, [boss ? (byte)1 : (byte)0])
        );
    }

    private static void Crystal_OnPlayer(
        On.Celeste.CrystalStaticSpinner.orig_OnPlayer orig,
        CrystalStaticSpinner self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void Crystal_OnHoldable(
        On.Celeste.CrystalStaticSpinner.orig_OnHoldable orig,
        CrystalStaticSpinner self,
        Holdable holdable
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, holdable);
    }

    private static void Dust_OnPlayer(
        On.Celeste.DustStaticSpinner.orig_OnPlayer orig,
        DustStaticSpinner self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }

    private static void Dust_OnHoldable(
        On.Celeste.DustStaticSpinner.orig_OnHoldable orig,
        DustStaticSpinner self,
        Holdable holdable
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, holdable);
    }
}
