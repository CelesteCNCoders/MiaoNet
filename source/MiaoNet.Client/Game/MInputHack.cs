namespace Celeste.Mod.MiaoNet;

// hmmm, just hack
internal static class MInputHack
{
    public static void ConsumeAllInput()
    {
        foreach(var input in MInput.VirtualInputs)
        {
            if (input is VirtualButton button)
                button.ConsumePress();
        }
    }
}

internal static class SpeedrunToolFix
{
    public static bool IsSceneNull(Entity entity)
        => entity.Scene is null;
}