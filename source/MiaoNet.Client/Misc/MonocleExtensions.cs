namespace Celeste.Mod.MiaoNet;

// deal with a monocle bug
internal static class MonocleExtensions
{
    /// <summary>
    /// If you <see cref="Scene.Add(Entity)"/> an entity and then
    /// <see cref="Scene.Remove(Entity)"/> it in one frame, then
    /// the entity is not removed actually.
    /// </summary>
    public static void CompletelyRemove(this Scene scene, Entity entity)
    {
        var list = scene.Entities;
        scene.Remove(entity);
        list.toAdd.Remove(entity);
        list.adding.Remove(entity);
    }

    /// <summary>
    /// If you <see cref="Entity.Add(Component)"/> an component and then
    /// <see cref="Entity.Remove(Component)"/> it in one frame, then
    /// the component is not removed actually.
    /// </summary>
    public static void CompletelyRemove(this Entity entity, Component component)
    {
        var list = entity.Components;
        list.Remove(entity);
        list.toAdd.Remove(component);
        list.adding.Remove(component);
    }
}
