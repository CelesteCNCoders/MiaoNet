using MiaoNet.Shared;

namespace Celeste.Mod.MiaoNet;

internal sealed class WatchRoomEntityIndex
{
    [ThreadStatic]
    private static WatchRoomEntityIndex? captureIndex;
    [ThreadStatic]
    private static int captureDepth;

    private static WatchRoomEntityIndex? activeIndex;

    private readonly DynamicTypeIndex<Entity> entities = new();
    private readonly List<Entity> pendingAdds = [];
    private readonly List<Entity> pendingRemoves = [];
    private Level? level;
    private string? room;
    private bool rebuildAfterNextUpdate;

    internal static void Load()
    {
        On.Monocle.EntityList.Add_Entity += EntityList_Add;
        On.Monocle.EntityList.Remove_Entity += EntityList_Remove;
        On.Monocle.EntityList.UpdateLists += EntityList_UpdateLists;
        On.Monocle.EntityList.ClearEntities += EntityList_ClearEntities;
    }

    internal static void Unload()
    {
        On.Monocle.EntityList.Add_Entity -= EntityList_Add;
        On.Monocle.EntityList.Remove_Entity -= EntityList_Remove;
        On.Monocle.EntityList.UpdateLists -= EntityList_UpdateLists;
        On.Monocle.EntityList.ClearEntities -= EntityList_ClearEntities;
        activeIndex?.Detach();
        activeIndex = null;
        captureIndex = null;
        captureDepth = 0;
    }

    internal IDisposable BeginCapture(Level targetLevel)
    {
        Attach(targetLevel);
        if (captureDepth > 0 && !ReferenceEquals(captureIndex, this))
            throw new InvalidOperationException("Nested watch captures used different room indexes.");
        captureIndex = this;
        captureDepth++;
        return new CaptureScope(this);
    }

    internal void Detach()
    {
        if (ReferenceEquals(activeIndex, this))
            activeIndex = null;
        level = null;
        room = null;
        rebuildAfterNextUpdate = false;
        pendingAdds.Clear();
        pendingRemoves.Clear();
        entities.Reset([]);
    }

    internal static IEnumerable<TEntity> Enumerate<TEntity>(Level targetLevel)
        where TEntity : Entity
    {
        WatchRoomEntityIndex? index = captureIndex ?? activeIndex;
        if (index is not null && index.OwnsCurrentRoom(targetLevel))
            return index.entities.Get<TEntity>();
        return targetLevel.Entities.OfType<TEntity>();
    }

    internal static bool IsCapturing(Level targetLevel)
        => captureIndex is { } index && index.Owns(targetLevel);

    private void Attach(Level targetLevel)
    {
        string targetRoom = targetLevel.Session.Level;
        if (Owns(targetLevel) && StringComparer.Ordinal.Equals(room, targetRoom))
            return;

        if (activeIndex is not null && !ReferenceEquals(activeIndex, this))
            activeIndex.Detach();
        activeIndex = this;
        level = targetLevel;
        room = targetRoom;
        pendingAdds.Clear();
        pendingRemoves.Clear();
        entities.Reset(targetLevel.Entities.ToArray());
        rebuildAfterNextUpdate = true;
    }

    private bool Owns(Level targetLevel)
        => ReferenceEquals(level, targetLevel);

    private bool OwnsCurrentRoom(Level targetLevel)
        => Owns(targetLevel) && StringComparer.Ordinal.Equals(room, targetLevel.Session.Level);

    private bool Owns(EntityList entityList)
        => level is not null && ReferenceEquals(level.Entities, entityList);

    private void RecordAdd(Entity? entity)
    {
        if (entity is not null)
            pendingAdds.Add(entity);
    }

    private void RecordRemove(Entity? entity)
    {
        if (entity is not null)
            pendingRemoves.Add(entity);
    }

    private void Commit(EntityList entityList)
    {
        if (level is null)
            return;

        if (rebuildAfterNextUpdate)
        {
            entities.Reset(entityList.ToArray());
            rebuildAfterNextUpdate = false;
        }
        else
        {
            foreach (Entity entity in pendingRemoves)
                entities.Remove(entity);
            foreach (Entity entity in pendingAdds)
            {
                if (ReferenceEquals(entity.Scene, level))
                    entities.Add(entity);
            }

            if (entities.Count != entityList.Count)
                entities.Reset(entityList.ToArray());
        }

        pendingAdds.Clear();
        pendingRemoves.Clear();
    }

    private void EndCapture()
    {
        if (captureDepth <= 0 || !ReferenceEquals(captureIndex, this))
            throw new InvalidOperationException("Unbalanced watch room index capture scope.");
        captureDepth--;
        if (captureDepth == 0)
            captureIndex = null;
    }

    private static void EntityList_Add(
        On.Monocle.EntityList.orig_Add_Entity orig,
        EntityList self,
        Entity entity
    )
    {
        orig(self, entity);
        if (activeIndex is { } index && index.Owns(self))
            index.RecordAdd(entity);
    }

    private static void EntityList_Remove(
        On.Monocle.EntityList.orig_Remove_Entity orig,
        EntityList self,
        Entity entity
    )
    {
        orig(self, entity);
        if (activeIndex is { } index && index.Owns(self))
            index.RecordRemove(entity);
    }

    private static void EntityList_UpdateLists(
        On.Monocle.EntityList.orig_UpdateLists orig,
        EntityList self
    )
    {
        orig(self);
        if (activeIndex is { } index && index.Owns(self))
            index.Commit(self);
    }

    private static void EntityList_ClearEntities(
        On.Monocle.EntityList.orig_ClearEntities orig,
        EntityList self
    )
    {
        orig(self);
        if (activeIndex is { } index && index.Owns(self))
            index.Detach();
    }

    private sealed class CaptureScope(WatchRoomEntityIndex owner) : IDisposable
    {
        private WatchRoomEntityIndex? owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref owner, null)?.EndCapture();
    }
}
