namespace Celeste.Mod.MiaoNet;

// hmmm, just hack
internal static class MInputHack
{
    public static void ConsumeAllInputs()
    {
        foreach(var input in MInput.VirtualInputs)
        {
            if (input is VirtualButton button)
                button.ConsumePress();
        }
    }
}