#pragma warning disable CA2211

using System.Diagnostics.CodeAnalysis;
using MonoMod.ModInterop;

namespace Celeste.Mod.MiaoNet;

[ModImportName("SpeedrunTool.SaveLoad")]
public static class SpeedrunToolInterop
{
    public static Action<Func<Type, bool>>? AddReturnSameObjectProcessor;
    public static Action<Func<Type, bool>>? RemoveReturnSameObjectProcessor;

    public delegate object RegisterSaveLoadActionHandler(
        Action<Dictionary<Type, Dictionary<string, object>>, Level>? saveState,
        Action<Dictionary<Type, Dictionary<string, object>>, Level>? loadState,
        Action? clearState,
        Action<Level>? beforeSaveState,
        Action<Level>? beforeLoadState,
        Action? preCloneEntities
    );

    public static RegisterSaveLoadActionHandler? RegisterSaveLoadAction;
    public static Action<object>? Unregister;

    public static Action<Entity, bool>? IgnoreSaveState;
}

public static class SpeedrunToolCompat
{
    private static readonly List<Entity> miaoNetEntities = new(4);
    private static object? saveLoadAction;

    public static void Load()
    {
        typeof(SpeedrunToolInterop).ModInterop();
        if (SpeedrunToolInterop.IgnoreSaveState is null)
            return;

        SpeedrunToolInterop.AddReturnSameObjectProcessor!(CanReturnSameObject);
        saveLoadAction = SpeedrunToolInterop.RegisterSaveLoadAction!(
            (_, level) => ReaddAllNetEntities(level),
            (_, level) => { ReaddAllNetEntities(level); MiaoNetModule.OnLoadState(level); },
            null, new(RemoveAllNetEntities), new(RemoveAllNetEntities), null
        );
    }

    public static void Unload()
    {
        if (SpeedrunToolInterop.IgnoreSaveState is null)
            return;

        SpeedrunToolInterop.RemoveReturnSameObjectProcessor!(CanReturnSameObject);
        SpeedrunToolInterop.Unregister!(saveLoadAction!);
    }

    private static bool CanReturnSameObject(Type type)
        => type.Assembly == typeof(MiaoNetContext).Assembly;

    // TODO remove these once SL fixed the bug
    private static void RemoveAllNetEntities(Level level)
    {
        miaoNetEntities.Clear();
        var list = level.Tracker.GetEntities<MiaoNetEntity>();
        miaoNetEntities.EnsureCapacity(list.Count);
        miaoNetEntities.AddRange(list);
        foreach (var entity in miaoNetEntities)
            level.RemoveImmediately(entity);
    }

    private static void ReaddAllNetEntities(Level level)
    {
        foreach (var entity in miaoNetEntities)
            level.AddImmediately(entity);

        miaoNetEntities.Clear();
    }

    private static void AddImmediately(this Level level, Entity entity)
    {
        EntityList entityList = level.Entities;

        if (entityList.current.Add(entity))
        {
            entityList.entities.Add(entity);
            level.TagLists.EntityAdded(entity);
            level.Tracker.EntityAdded(entity);
            entity.BasedAdded(level);
        }
    }

    private static void RemoveImmediately(this Level level, Entity entity)
    {
        EntityList entityList = level.Entities;

        if (entityList.current.Remove(entity))
        {
            entityList.entities.Remove(entity);
            entityList.toRemove.Remove(entity);
            entity.BasedRemoved(level);

            level.TagLists.EntityRemoved(entity);
            level.Tracker.EntityRemoved(entity);
            Engine.Pooler.EntityRemoved(entity);
        }
    }

    private static void BasedAdded(this Entity entity, Level level)
    {
        entity.Scene = level;
        if (entity.Components != null)
        {
            foreach (Component component in entity.Components)
            {
                component.EntityAdded(level);
            }
        }

        level.SetActualDepth(entity);
    }

    private static void BasedRemoved(this Entity entity, Level level)
    {
        if (entity.Components != null)
        {
            foreach (Component component in entity.Components)
            {
                component.EntityRemoved(level);
            }
        }

        entity.Scene = null;
    }
}