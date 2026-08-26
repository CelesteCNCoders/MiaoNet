using System.Runtime.CompilerServices;

namespace Celeste.Mod.MiaoNet;

public static class TouchSwitchIDTracker
{
    private sealed class IDHolder
    {
        public int ID { get; }

        public IDHolder(int id)
        {
            ID = id;
        }
    }

    private readonly static ConditionalWeakTable<TouchSwitch, IDHolder> table = new();

    public static void Load()
    {
        On.Celeste.TouchSwitch.ctor_EntityData_Vector2 += TouchSwitch_ctor;
    }

    public static void Unload()
    {
        On.Celeste.TouchSwitch.ctor_EntityData_Vector2 -= TouchSwitch_ctor;
        table.Clear();
    }

    public static bool TryGetID(TouchSwitch touchSwitch, out int id)
    {
        if (table.TryGetValue(touchSwitch, out IDHolder? holder))
        {
            id = holder.ID;
            return true;
        }

        id = default;
        return false;
    }

    private static void TouchSwitch_ctor(
        On.Celeste.TouchSwitch.orig_ctor_EntityData_Vector2 orig,
        TouchSwitch self,
        EntityData data,
        Vector2 offset
    )
    {
        orig(self, data, offset);
        table.AddOrUpdate(self, new IDHolder(data.ID));
    }
}
