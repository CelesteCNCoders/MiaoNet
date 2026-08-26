using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace Celeste.Mod.MiaoNet;

/// <summary>
/// Prevents the hidden local Player from becoming an authoritative gameplay or
/// cutscene trigger source while it is being used as the Watcher camera anchor.
/// The state is restored in a finally block so transitions and stop-watching
/// scene restoration continue to see the real local Player normally.
/// </summary>
internal static class WatchTriggerFirewall
{
    public static void BeginWatching(Level level)
    {
        if (level.Tracker.GetEntity<Player>() is { } player)
            player.triggersInside.Clear();
        foreach (Trigger trigger in level.Tracker.GetEntities<Trigger>().Cast<Trigger>())
            trigger.Triggered = false;
        foreach (BirdTutorialGui tutorial in level.Entities.OfType<BirdTutorialGui>())
        {
            tutorial.Open = false;
            tutorial.Visible = false;
            tutorial.Active = false;
        }
    }

    public static void Load()
    {
        On.Celeste.Player.Update += Player_Update;
        On.Celeste.EventTrigger.OnEnter += EventTrigger_OnEnter;
        IL.Monocle.EntityList.Update += EntityList_Update;
    }

    public static void Unload()
    {
        IL.Monocle.EntityList.Update -= EntityList_Update;
        On.Celeste.EventTrigger.OnEnter -= EventTrigger_OnEnter;
        On.Celeste.Player.Update -= Player_Update;
    }

    private static void EntityList_Update(ILContext il)
    {
        ILCursor cursor = new(il);
        while (cursor.TryGotoNext(MoveType.Before,
            instruction => instruction.MatchCallvirt<Monocle.Entity>(nameof(Monocle.Entity.Update))))
        {
            cursor.Remove();
            cursor.EmitDelegate(static (Monocle.Entity entity) =>
            {
                if (MiaoNetModule.IsWatching && entity is NPC or AscendManager
                    or IntroCar or Bonfire or Payphone or Lookout or BirdTutorialGui)
                {
                    if (entity is BirdTutorialGui tutorial)
                    {
                        tutorial.Open = false;
                        tutorial.Visible = false;
                        tutorial.Active = false;
                        return;
                    }
                    if (entity is NPC npc)
                        WatchNarrativeNPCAdapter.UpdatePresentation(npc);
                    else if (entity is IntroCar car)
                        WatchIntroCarAdapter.UpdatePresentation(car);
                    if (!MiaoNetModule.IsWatchedPlayerPaused)
                        UpdatePresentationComponents(entity);
                    return;
                }
                entity.Update();
            });
        }
    }

    private static void UpdatePresentationComponents(Entity entity)
    {
        foreach (Component component in entity.Components.ToArray())
        {
            if (component.Active && component is Sprite or Image or Wiggler
                or Shaker or SineWave or VertexLight or BloomPoint)
                component.Update();
        }
    }

    private static void Player_Update(On.Celeste.Player.orig_Update orig, Player self)
    {
        if (!MiaoNetModule.IsWatching)
        {
            orig(self);
            return;
        }

        bool collidable = self.Collidable;
        self.Collidable = false;
        try
        {
            orig(self);
        }
        finally
        {
            self.Collidable = collidable;
        }
    }

    private static void EventTrigger_OnEnter(
        On.Celeste.EventTrigger.orig_OnEnter orig,
        EventTrigger self,
        Player player
    )
    {
        if (!MiaoNetModule.IsWatching)
            orig(self, player);
    }
}
