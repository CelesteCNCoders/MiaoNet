namespace Celeste.Mod.MiaoNet;

public static class MiaoNetCommands
{
    private static MiaoNetContext Context => MiaoNetModule.Instance.MiaoNetContext;

    [Command("con", "Connect to MiaoNet.")]
    public static void Connect(string? server = null)
    {
        if (server is not null)
            Context.TargetServer = server;
        Context.Connect();
    }

    [Command("dc", "Disconnect from MiaoNet.")]
    public static void Disconnect()
    {
        Context.Disconnect();
    }

#if DEBUG
    [Command("mn_status", "Show a MiaoNet status message.")]
    public static void MiaoNet_ShowStatus(string text)
    {
        Context.StatusComponent.ShowStatusMessage(text);
    }
#endif
}