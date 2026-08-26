using System.Reflection;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.MiaoNet;

internal static class BitsboltsCompat
{
    private delegate void RenderEntityWithOrig(object self, Entity entity);
    private delegate void MainComponentOnDisconnectedOrig(MainComponent self);

    private static Hook? renderEntityWithHook;
    private static Hook? mainComponentOnDisconnectedHook;

    public static void Load()
    {
        EverestModule? module = Everest.Modules.FirstOrDefault(m =>
            string.Equals(m.Metadata.Name, "bitsbolts", StringComparison.OrdinalIgnoreCase));
        if (module is null)
            return;

        MethodInfo? target = module.GetType().Assembly
            .GetType("Bitsbolts.Components.WorldCamera")?
            .GetMethod("RenderEntityWith", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo? detour = typeof(BitsboltsCompat).GetMethod(
            nameof(RenderEntityWith), BindingFlags.NonPublic | BindingFlags.Static);
        if (target is null || detour is null)
            return;

        renderEntityWithHook = new Hook(target, detour);

        MethodInfo? disconnectTarget = typeof(MainComponent).GetMethod(
            nameof(MainComponent.OnDisconnected), BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? disconnectDetour = typeof(BitsboltsCompat).GetMethod(
            nameof(MainComponentOnDisconnected), BindingFlags.NonPublic | BindingFlags.Static);
        if (disconnectTarget is not null && disconnectDetour is not null)
            mainComponentOnDisconnectedHook = new Hook(disconnectTarget, disconnectDetour);
    }

    public static void Unload()
    {
        renderEntityWithHook?.Dispose();
        renderEntityWithHook = null;
        mainComponentOnDisconnectedHook?.Dispose();
        mainComponentOnDisconnectedHook = null;
    }

    private static void RenderEntityWith(RenderEntityWithOrig orig, object self, Entity entity)
    {
        if (entity is GhostRenderLayerEntity)
            return;

        orig(self, entity);
    }

    private static void MainComponentOnDisconnected(
        MainComponentOnDisconnectedOrig orig, MainComponent self)
    {
        orig(self);

        if (Engine.Scene is not Level level)
            return;

        // GhostDeadBody is tracked through MiaoNetGhostEntity's inherited tracker
        // entry, so an exact GhostDeadBody entry does not necessarily exist.
        foreach (GhostDeadBody body in level.Tracker
            .GetEntities<MiaoNetGhostEntity>()
            .OfType<GhostDeadBody>()
            .ToArray())
            body.RemoveSelf();
    }
}
