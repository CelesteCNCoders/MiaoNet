namespace Celeste.Mod.MiaoNet;

public static class MiaoNetCommands
{
    [Command("con", "Connect to MiaoNet.")]
    public static void Connect()
    {
        MiaoNetModule.Instance.MiaoNetContext.Connect();
    }

    [Command("dc", "Disconnect MiaoNet.")]
    public static void Disconnect()
    {
        MiaoNetModule.Instance.MiaoNetContext.Disconnect();
    }
}